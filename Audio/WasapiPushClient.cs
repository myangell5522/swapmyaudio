using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace swapmyaudio.Audio
{
	internal enum PushResult
	{
		/// <summary>Block was queued on the target device.</summary>
		Written,
		/// <summary>Target queue is full (clock drift or a hitch); the block was dropped on purpose.</summary>
		Dropped,
		/// <summary>The target device is unusable (unplugged, disabled, disposed).</summary>
		Failed
	}

	internal sealed class WasapiPushClient : IDisposable
	{
		private const int AudclntEDeviceInvalidated = unchecked((int)0x88890004);
		private const double MaxQueuedSeconds = 0.06;

		private IntPtr _client;
		private IntPtr _render;
		private uint _bufferFrames;
		private uint _maxQueuedFrames;
		private float[] _scratch;
		private readonly object _gate = new();

		// Resolved once in Open(). Marshal.GetDelegateForFunctionPointer allocates,
		// and Write() runs on the audio thread about 100 times a second.
		private NativeMethods.GetCurrentPaddingDelegate _getPadding;
		private NativeMethods.RenderGetBufferDelegate _getBuffer;
		private NativeMethods.RenderReleaseBufferDelegate _releaseBuffer;

		internal string LastError { get; private set; } = "";

		/// <summary>Set once the device reports AUDCLNT_E_DEVICE_INVALIDATED.</summary>
		internal bool Lost { get; private set; }

		internal static WasapiPushClient TryOpen(string deviceId, ushort channels, uint sampleRate, out string error)
		{
			error = "";
			var client = new WasapiPushClient();
			if (!client.Open(deviceId, channels, sampleRate)) {
				error = client.LastError;
				client.Dispose();
				return null;
			}

			return client;
		}

		internal PushResult Write(IntPtr source, int frames, int channels)
		{
			if (source == IntPtr.Zero || frames <= 0 || channels <= 0)
				return PushResult.Failed;

			// Never block the audio thread. If Dispose() holds the lock we just pass the block through.
			if (!Monitor.TryEnter(_gate, 0))
				return PushResult.Failed;

			try {
				if (_client == IntPtr.Zero || _render == IntPtr.Zero || Lost)
					return PushResult.Failed;

				int hr = _getPadding(_client, out uint padding);
				if (hr < 0)
					return Fail(hr);

				// The FAudio clock drives us, the target device has its own clock. If the target is
				// slower, the queue grows; cap it so latency can't creep up to the full 500 ms buffer.
				if (padding > _maxQueuedFrames)
					return PushResult.Dropped;

				uint available = _bufferFrames > padding ? _bufferFrames - padding : 0;
				uint write = Math.Min((uint)frames, available);
				if (write == 0)
					return PushResult.Dropped;

				hr = _getBuffer(_render, write, out IntPtr dest);
				if (hr < 0 || dest == IntPtr.Zero)
					return Fail(hr);

				int floats = (int)write * channels;
				if (_scratch == null || _scratch.Length < floats)
					return PushResult.Failed;

				Marshal.Copy(source, _scratch, 0, floats);
				Marshal.Copy(_scratch, 0, dest, floats);
				_releaseBuffer(_render, write, 0);
				return PushResult.Written;
			}
			catch {
				return PushResult.Failed;
			}
			finally {
				Monitor.Exit(_gate);
			}
		}

		private PushResult Fail(int hr)
		{
			if (hr == AudclntEDeviceInvalidated)
				Lost = true;
			return PushResult.Failed;
		}

		public void Dispose()
		{
			lock (_gate) {
				if (_client != IntPtr.Zero) {
					try {
						ComVtable.Fn<NativeMethods.AudioClientSimpleDelegate>(_client, NativeMethods.AudioClientStopSlot)(_client);
					}
					catch {
					}
				}

				ComVtable.Release(_render);
				_render = IntPtr.Zero;
				ComVtable.Release(_client);
				_client = IntPtr.Zero;
			}
		}

		private bool Open(string deviceId, ushort channels, uint sampleRate)
		{
			Guid clsid = NativeMethods.MmDeviceEnumeratorClsid;
			Guid iid = NativeMethods.ImmDeviceEnumeratorIid;
			int hr = NativeMethods.CoCreateInstance(ref clsid, IntPtr.Zero, NativeMethods.ClsCtxAll, ref iid, out IntPtr enumerator);
			if (hr < 0 || enumerator == IntPtr.Zero) {
				LastError = "CoCreate 0x" + hr.ToString("X8");
				return false;
			}

			IntPtr device = IntPtr.Zero;
			try {
				hr = ComVtable.Fn<NativeMethods.GetDeviceDelegate>(enumerator, NativeMethods.EnumeratorGetDeviceSlot)(enumerator, deviceId, out device);
				if (hr < 0 || device == IntPtr.Zero) {
					LastError = "GetDevice 0x" + hr.ToString("X8");
					return false;
				}

				Guid clientIid = NativeMethods.AudioClientIid;
				hr = ComVtable.Fn<NativeMethods.ActivateDelegate>(device, NativeMethods.DeviceActivateSlot)(device, ref clientIid, NativeMethods.ClsCtxAll, IntPtr.Zero, out _client);
				if (hr < 0 || _client == IntPtr.Zero) {
					LastError = "Activate 0x" + hr.ToString("X8");
					_client = IntPtr.Zero;
					return false;
				}

				WaveFormatEx format = WaveFormatEx.IeeeFloat(channels, sampleRate);
				IntPtr pFormat = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
				try {
					Marshal.StructureToPtr(format, pFormat, false);
					uint flags = NativeMethods.AudClntStreamflagsAutoconvertPcm | NativeMethods.AudClntStreamflagsSrcDefaultQuality;
					hr = ComVtable.Fn<NativeMethods.AudioClientInitializeDelegate>(_client, NativeMethods.AudioClientInitializeSlot)(
						_client, 0, flags, 5000000, 0, pFormat, IntPtr.Zero);
					if (hr < 0) {
						LastError = "Initialize 0x" + hr.ToString("X8");
						return false;
					}
				}
				finally {
					Marshal.FreeHGlobal(pFormat);
				}

				Guid renderIid = NativeMethods.AudioRenderClientIid;
				hr = ComVtable.Fn<NativeMethods.GetServiceDelegate>(_client, NativeMethods.AudioClientGetServiceSlot)(_client, ref renderIid, out _render);
				if (hr < 0 || _render == IntPtr.Zero) {
					LastError = "GetService 0x" + hr.ToString("X8");
					_render = IntPtr.Zero;
					return false;
				}

				hr = ComVtable.Fn<NativeMethods.GetBufferSizeDelegate>(_client, NativeMethods.AudioClientGetBufferSizeSlot)(_client, out _bufferFrames);
				if (hr < 0) {
					LastError = "GetBufferSize 0x" + hr.ToString("X8");
					return false;
				}

				hr = ComVtable.Fn<NativeMethods.AudioClientSimpleDelegate>(_client, NativeMethods.AudioClientStartSlot)(_client);
				if (hr < 0) {
					LastError = "Start 0x" + hr.ToString("X8");
					return false;
				}

				_getPadding = ComVtable.Fn<NativeMethods.GetCurrentPaddingDelegate>(_client, NativeMethods.AudioClientGetCurrentPaddingSlot);
				_getBuffer = ComVtable.Fn<NativeMethods.RenderGetBufferDelegate>(_render, NativeMethods.RenderClientGetBufferSlot);
				_releaseBuffer = ComVtable.Fn<NativeMethods.RenderReleaseBufferDelegate>(_render, NativeMethods.RenderClientReleaseBufferSlot);
				_maxQueuedFrames = Math.Min(_bufferFrames, (uint)(sampleRate * MaxQueuedSeconds));
				// Allocated once here, so Write() on the audio thread never allocates.
				_scratch = new float[(int)Math.Max(_bufferFrames, 1) * Math.Max((int)channels, 1)];
				LastError = "";
				return true;
			}
			finally {
				ComVtable.Release(device);
				ComVtable.Release(enumerator);
			}
		}
	}
}
