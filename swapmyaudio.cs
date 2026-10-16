using swapmyaudio.UI;
using Terraria.ModLoader;

namespace swapmyaudio
{
	public class swapmyaudio : Mod
	{
		/// <summary>
		/// Cross-mod API so settings overlays that replace the vanilla Volume page
		/// (e.g. The End Of A Calamity Menu Theme / DieWithASmile) can show the device picker:
		///   Call("ApiVersion")          -> int
		///   Call("Supported")           -> bool, false on non-Windows or if WASAPI failed
		///   Call("Label")               -> localized "Audio Device"
		///   Call("Value")               -> "Device name  2/3" (also polls for device changes, throttled)
		///   Call("Cycle", int delta, bool playSound = true) -> bool, true if the device actually switched
		/// </summary>
		public override object Call(params object[] args) => VolumeDeviceMenu.HandleCall(args);
	}
}
