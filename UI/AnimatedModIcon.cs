using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader;

namespace swapmyaudio.UI
{
	/// <summary>
	/// tModLoader only shows icon.png as a still image. While this mod is loaded, draw the
	/// frames from icon_anim.png over our entry in the Mods list so the icon animates there too.
	/// </summary>
	public class AnimatedModIcon : ModSystem
	{
		private const int FrameSize = 80;
		private const int Columns = 8;
		private const int FrameCount = 64;
		private const double FrameSeconds = 0.055; // same timing as the Workshop GIF

		private delegate void OrigDraw(object self, SpriteBatch spriteBatch);
		private delegate void HookDraw(OrigDraw orig, object self, SpriteBatch spriteBatch);

		private static Asset<Texture2D> _sheet;
		private static FieldInfo _iconField;
		private static PropertyInfo _modNameProperty;
		private static readonly Stopwatch Clock = Stopwatch.StartNew();

		public override void Load()
		{
			if (Main.dedServ)
				return;

			try {
				Type modItem = typeof(Mod).Assembly.GetType("Terraria.ModLoader.UI.UIModItem");
				MethodInfo draw = modItem?.GetMethod("Draw", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(SpriteBatch) }, null);
				_iconField = modItem?.GetField("_modIcon", BindingFlags.NonPublic | BindingFlags.Instance);
				_modNameProperty = modItem?.GetProperty("ModName", BindingFlags.Public | BindingFlags.Instance);

				if (draw == null || _iconField == null || _modNameProperty == null) {
					Mod.Logger.Info("Animated icon off: Mods list layout changed in this tModLoader version.");
					return;
				}

				_sheet = ModContent.Request<Texture2D>("swapmyaudio/icon_anim");
				MonoModHooks.Add(draw, (HookDraw)DrawHook);
			}
			catch (Exception e) {
				Mod.Logger.Warn("Animated icon off: " + e.Message);
			}
		}

		public override void Unload()
		{
			_sheet = null;
			_iconField = null;
			_modNameProperty = null;
		}

		private static void DrawHook(OrigDraw orig, object self, SpriteBatch spriteBatch)
		{
			orig(self, spriteBatch);

			try {
				if (_sheet == null || !_sheet.IsLoaded)
					return;
				if (_modNameProperty.GetValue(self) is not string name || name != nameof(swapmyaudio))
					return;
				if (_iconField.GetValue(self) is not UIImage icon)
					return;

				int frame = (int)(Clock.Elapsed.TotalSeconds / FrameSeconds) % FrameCount;
				var source = new Rectangle(frame % Columns * FrameSize, frame / Columns * FrameSize, FrameSize, FrameSize);
				Rectangle target = icon.GetDimensions().ToRectangle();
				spriteBatch.Draw(_sheet.Value, target, source, Color.White);
			}
			catch {
				// Never break the Mods menu over a cosmetic effect.
			}
		}
	}
}
