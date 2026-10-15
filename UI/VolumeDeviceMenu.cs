using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using swapmyaudio.Audio;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace swapmyaudio.UI
{
	public class VolumeDeviceMenu : ModSystem
	{
		private const int TitleVolumeMenuMode = 26;
		// Full endpoint enumeration (COM + property store reads) on the draw thread; twice a second was overkill.
		private const int PollInterval = 90;

		private static AudioOutputService _service;
		private static readonly List<Entry> Entries = new();

		private static bool _optionsVolumePage;
		private static bool _wasTitlePage;
		private static bool _wasOptionsPage;
		private static bool _armed;
		private static bool _drawingPicker;
		private static bool _mouseHeld;
		private static bool _pressed;
		private static bool _sawAmbient;
		private static bool _spacerTaken;
		private static bool _filledSpacer;
		private static bool _headerSaved;
		private static bool _volumeRowSaved;
		private static int _bars;
		private static int _barDepth;
		private static int _extraVolumeRows;
		private static int _ambientIndex = -1;
		private static int _headerIndex;
		private static int _hoverSide;
		private static int _poll;
		private static int _index;
		private static float _volumeScale = 1f;
		private static float _volumeColorScale;
		private static Vector2 _headerAnchor;
		private static Vector2 _headerOffset;
		private static Vector2 _volumeAnchor;
		private static Vector2 _volumeOffset;

		public override void Load()
		{
			if (!OperatingSystem.IsWindows())
				return;

			try {
				_service = new AudioOutputService();
			}
			catch (Exception e) {
				Mod.Logger.Error("Audio service failed: " + e);
				_service = null;
				return;
			}

			if (_service == null || !_service.Supported)
				return;

			RebuildEntries();
			var log = Mod.Logger;
			if (!string.IsNullOrEmpty(_service.LastError))
				log.Warn("Audio enum: " + _service.LastError);
			log.Info($"Audio devices: {_service.Devices.Count} ({_service.SummarizeDevices()})");

			On_Main.DrawMenu += DrawMenuHook;
			On_IngameOptions.Draw += DrawOptionsHook;
			On_IngameOptions.DrawRightSide += DrawRightSideHook;
			On_IngameOptions.DrawValueBar += DrawValueBarHook;
		}

		public override void Unload()
		{
			_service?.Dispose();
			_service = null;
			Entries.Clear();
		}

		private static void DrawMenuHook(On_Main.orig_DrawMenu orig, Main self, GameTime time)
		{
			_bars = 0;
			_barDepth = 0;
			_extraVolumeRows = 0;
			TickMouse();
			bool page = Main.gameMenu && Main.menuMode == TitleVolumeMenuMode;
			if (page)
				BeginVolumePage(!_wasTitlePage);
			else if (_wasTitlePage)
				EndVolumePage();

			orig(self, time);
			_wasTitlePage = page;
		}

		private static void DrawOptionsHook(On_IngameOptions.orig_Draw orig, Main main, SpriteBatch sb)
		{
			_optionsVolumePage = false;
			_bars = 0;
			_barDepth = 0;
			_extraVolumeRows = 0;
			_sawAmbient = false;
			_spacerTaken = false;
			_filledSpacer = false;
			_headerSaved = false;
			_volumeRowSaved = false;
			_ambientIndex = -1;
			TickMouse();
			if (!Main.gameMenu && IngameOptions.category == 0)
				BeginVolumePage(!_wasOptionsPage);

			orig(main, sb);

			if (_optionsVolumePage && _spacerTaken && !_filledSpacer)
				DrawHeaderPicker(sb);

			if (_optionsVolumePage)
				_wasOptionsPage = true;
			else if (_wasOptionsPage) {
				EndVolumePage();
				_wasOptionsPage = false;
			}
		}

		private static bool DrawRightSideHook(
			On_IngameOptions.orig_DrawRightSide orig,
			SpriteBatch sb,
			string txt,
			int i,
			Vector2 anchor,
			Vector2 offset,
			float scale,
			float colorScale,
			Color over)
		{
			if (!_drawingPicker && !string.IsNullOrEmpty(txt) && txt == Lang.menu[65].Value) {
				_headerSaved = true;
				_headerAnchor = anchor;
				_headerOffset = offset;
				_headerIndex = i;
			}

			if (!_drawingPicker && IsVolumePercentRow(txt)) {
				_optionsVolumePage = true;
				_volumeAnchor = anchor;
				_volumeOffset = offset;
				_volumeScale = scale;
				_volumeColorScale = colorScale;
				_volumeRowSaved = true;
				if (txt.StartsWith(Lang.menu[119].Value, StringComparison.Ordinal)) {
					_sawAmbient = true;
					_ambientIndex = i;
				}
			}

			if (!_drawingPicker && _sawAmbient && i == _ambientIndex + 1 && !string.IsNullOrEmpty(txt))
				_spacerTaken = true;

			if (!_drawingPicker && _service != null && _sawAmbient && !_spacerTaken &&
			    string.IsNullOrEmpty(txt) && i == _ambientIndex + 1) {
				return DrawNativePickerRow(orig, sb, i, anchor, offset, scale, colorScale);
			}

			return orig(sb, txt, i, anchor, offset, scale, colorScale, over);
		}

		private static float DrawValueBarHook(On_IngameOptions.orig_DrawValueBar orig, SpriteBatch sb, float scale, float perc, int lockState, Utils.ColorLerpMethod colorMethod)
		{
			_barDepth++;
			try {
				bool title = Main.gameMenu && Main.menuMode == TitleVolumeMenuMode;
				Vector2 row = IngameOptions.valuePosition;
				float result = orig(sb, scale, perc, lockState, colorMethod);
				if (_drawingPicker)
					return result;

				if (_barDepth != 1) {
					if (title || _optionsVolumePage)
						_extraVolumeRows++;
					return result;
				}

				if (!title)
					return result;

				_bars++;
				if (_bars == 3)
					DrawTitlePicker(sb, row);

				return result;
			}
			finally {
				_barDepth--;
			}
		}

		private static bool DrawNativePickerRow(
			On_IngameOptions.orig_DrawRightSide orig,
			SpriteBatch sb,
			int i,
			Vector2 anchor,
			Vector2 offset,
			float scale,
			float colorScale)
		{
			if (Entries.Count == 0)
				RebuildEntries();

			if (_volumeRowSaved) {
				anchor = _volumeAnchor;
				offset = _volumeOffset;
			}

			GetVolumeLook(i, scale, colorScale, out float rowScale, out float rowColor);
			DynamicSpriteFont font = FontAssets.MouseText.Value;
			string line = Truncate(font, BuildLine(includeLabel: true), rowScale, 230f);
			float ourHalf = font.MeasureString(line).X * rowScale * 0.5f;
			float musicHalf = font.MeasureString(Lang.menu[99].Value + " 100%").X * rowScale * 0.5f;
			anchor.X += Math.Max(0f, ourHalf - musicHalf) + 12f;
			Vector2 pos = anchor + offset * (1 + i);
			Vector2 size = font.MeasureString(line) * rowScale;
			Rectangle hit = new(
				(int)(pos.X - size.X * 0.5f),
				(int)(pos.Y - size.Y * 0.5f),
				(int)size.X,
				(int)size.Y);
			bool hovered = hit.Contains(Main.mouseX, Main.mouseY);

			_drawingPicker = true;
			bool result;
			try {
				result = orig(sb, line, i, anchor, offset, rowScale, rowColor, default);
			}
			finally {
				_drawingPicker = false;
			}

			if (hovered && IngameOptions.rightLock == -1)
				IngameOptions.rightHover = i;

			HandleHover(hit, hovered);
			_filledSpacer = true;
			return result || hovered;
		}

		private static void DrawHeaderPicker(SpriteBatch sb)
		{
			if (_service == null || !_headerSaved)
				return;

			if (Entries.Count == 0)
				RebuildEntries();

			if (_headerIndex < 0 || _headerIndex >= IngameOptions.rightScale.Length)
				return;

			GetVolumeLook(_headerIndex, _volumeScale, _volumeColorScale, out float rowScale, out float rowColor);
			Vector2 center = _headerAnchor + _headerOffset * (1 + _headerIndex);
			string header = Lang.menu[65].Value;
			Vector2 headerSize = FontAssets.MouseText.Value.MeasureString(header) * rowScale;
			string line = Truncate(FontAssets.MouseText.Value, BuildLine(includeLabel: false), rowScale, 220f);
			Vector2 pos = new(center.X + headerSize.X * 0.5f + 18f, center.Y);
			Vector2 size = FontAssets.MouseText.Value.MeasureString(line) * rowScale;
			Rectangle hit = new((int)pos.X, (int)(pos.Y - size.Y * 0.5f), (int)size.X, (int)size.Y);
			bool hovered = hit.Contains(Main.mouseX, Main.mouseY);
			Color color = Color.Lerp(Color.Gray, Color.White, rowColor);
			Utils.DrawBorderString(sb, line, pos, color, rowScale, 0f, 0.5f, -1);
			HandleHover(hit, hovered);
		}

		private static void DrawTitlePicker(SpriteBatch sb, Vector2 row)
		{
			if (_service == null)
				return;

			if (Entries.Count == 0)
				RebuildEntries();

			DynamicSpriteFont font = FontAssets.DeathText.Value;
			const float scale = 0.5f;
			float y = row.Y + 36f + _extraVolumeRows * 40f;
			float x = Main.screenWidth * 0.5f;
			string line = Truncate(font, BuildLine(includeLabel: true), scale, Main.screenWidth * 0.72f);
			Vector2 size = font.MeasureString(line) * scale;
			Rectangle hit = new((int)(x - size.X * 0.5f) - 10, (int)(y - size.Y * 0.5f) - 4, (int)size.X + 20, (int)size.Y + 8);
			bool hovered = hit.Contains(Main.mouseX, Main.mouseY);
			Color color = hovered ? new Color(255, 215, 0) : Color.White;
			float drawScale = scale * (hovered ? 1.06f : 1f);
			Utils.DrawBorderStringFourWay(sb, font, line, x, y, color, Color.Black, font.MeasureString(line) * 0.5f, drawScale);
			HandleHover(hit, hovered);
		}

		private static void GetVolumeLook(int i, float fallbackScale, float fallbackColor, out float scale, out float colorScale)
		{
			float minScale = fallbackScale - fallbackColor * 0.001f;
			if (_volumeRowSaved)
				minScale = _volumeScale - _volumeColorScale * 0.001f;

			scale = fallbackScale;
			if (i >= 0 && i < IngameOptions.rightScale.Length)
				scale = IngameOptions.rightScale[i];
			if (scale < minScale)
				scale = minScale;

			colorScale = MathHelper.Clamp((scale - minScale) / 0.001f, 0f, 1f);
		}

		private static void HandleHover(Rectangle hit, bool hovered)
		{
			int side = 0;
			if (hovered) {
				Main.blockMouse = true;
				IngameOptions.noSound = true;
				if (IngameOptions.rightLock == -1)
					IngameOptions.notBar = true;
				side = Main.mouseX < hit.Center.X ? -1 : 1;
				int wheel = PlayerInput.ScrollWheelDelta;
				if (wheel == 0)
					wheel = PlayerInput.ScrollWheelDeltaForUI;
				if (wheel != 0) {
					Cycle(wheel > 0 ? -1 : 1);
					PlayerInput.ScrollWheelDelta = 0;
					PlayerInput.ScrollWheelDeltaForUI = 0;
				}
			}

			if (side != _hoverSide) {
				if (side != 0)
					SoundEngine.PlaySound(SoundID.MenuTick);
				_hoverSide = side;
			}

			if (_armed && hovered && _pressed) {
				Main.mouseLeftRelease = false;
				Cycle(side == 0 ? 1 : side);
			}
		}

		private static void BeginVolumePage(bool entered)
		{
			if (entered) {
				_armed = !Main.mouseLeft;
				_hoverSide = 0;
				_poll = 0;
			}
			else if (!Main.mouseLeft) {
				_armed = true;
			}

			_poll++;
			bool lost = _service != null && _service.RecoverLostDevice();
			if (lost)
				_index = 0; // "System Default"; otherwise RebuildEntries() would re-select the dead device
			bool dirty = _service != null && _service.DevicesDirty;
			if (entered || lost || dirty || _poll >= PollInterval) {
				_poll = 0;
				_service?.Refresh(forceDevices: true);
				RebuildEntries();
			}
		}

		private static void EndVolumePage()
		{
			_armed = false;
			_hoverSide = 0;
		}

		private static void TickMouse()
		{
			_pressed = Main.mouseLeft && !_mouseHeld;
			_mouseHeld = Main.mouseLeft;
		}

		private static void RebuildEntries()
		{
			string keep = "";
			if (_index >= 0 && _index < Entries.Count)
				keep = Entries[_index].Id;
			else if (_service != null)
				keep = _service.SelectedId;

			Entries.Clear();
			Entries.Add(new Entry("", L("SystemDefault")));
			if (_service != null) {
				foreach (PlaybackDevice device in _service.Devices)
					Entries.Add(new Entry(device.Id, device.Name));
			}

			_index = 0;
			for (int i = 0; i < Entries.Count; i++) {
				if (string.Equals(Entries[i].Id, keep, StringComparison.OrdinalIgnoreCase)) {
					_index = i;
					break;
				}
			}
		}

		private static string BuildLine(bool includeLabel)
		{
			string text = "< " + CurrentName() + " > " + (_index + 1) + "/" + Math.Max(1, Entries.Count);
			return includeLabel ? L("AudioDevice") + ": " + text : text;
		}

		private static bool IsVolumePercentRow(string txt)
		{
			return !string.IsNullOrEmpty(txt) && txt.Contains('%') &&
			       (txt.StartsWith(Lang.menu[99].Value, StringComparison.Ordinal) ||
			        txt.StartsWith(Lang.menu[98].Value, StringComparison.Ordinal) ||
			        txt.StartsWith(Lang.menu[119].Value, StringComparison.Ordinal));
		}

		private static string CurrentName()
		{
			if (Entries.Count == 0)
				return L("SystemDefault");

			int i = _index;
			if (i < 0 || i >= Entries.Count)
				i = 0;
			return TruncateName(Entries[i].Name);
		}

		private static string TruncateName(string name)
		{
			if (string.IsNullOrEmpty(name) || name.Length <= 28)
				return name;
			return name[..25] + "...";
		}

		private static void Cycle(int delta)
		{
			if (_service == null || Entries.Count == 0 || delta == 0)
				return;

			int previous = _index;
			_index = ((_index + delta) % Entries.Count + Entries.Count) % Entries.Count;

			var log = ModContent.GetInstance<swapmyaudio>().Logger;
			bool ok;
			try {
				ok = _service.SelectDevice(Entries[_index].Id);
			}
			catch (Exception e) {
				log.Error("Set device crashed: " + e);
				_index = previous;
				return;
			}

			SoundEngine.PlaySound(SoundID.MenuTick);
			if (!ok) {
				// Don't show a device we failed to switch to.
				_index = previous;
				if (!string.IsNullOrEmpty(_service.LastError))
					log.Warn("Set device: " + _service.LastError);
			}
			else if (!string.IsNullOrEmpty(FAudioOutputTap.LastStatus)) {
				log.Info(FAudioOutputTap.LastStatus);
			}
		}

		private static string Truncate(DynamicSpriteFont font, string text, float scale, float maxWidth)
		{
			if (string.IsNullOrEmpty(text) || font.MeasureString(text).X * scale <= maxWidth)
				return text;

			const string ellipsis = "...";
			while (text.Length > 0 && font.MeasureString(text + ellipsis).X * scale > maxWidth)
				text = text[..^1];
			return text + ellipsis;
		}

		private static string L(string key) => Language.GetTextValue("Mods.swapmyaudio.UI." + key);

		private readonly struct Entry
		{
			internal Entry(string id, string name)
			{
				Id = id;
				Name = name;
			}

			internal string Id { get; }
			internal string Name { get; }
		}
	}
}
