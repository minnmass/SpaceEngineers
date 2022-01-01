using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;

namespace Utilities {
	public class Logger {
		private readonly IMyProgrammableBlock Me;

		public Logger(IMyProgrammableBlock me) {
			Me = me;
		}

		public void Log(string text, bool append = true) {
			if (Me == null) {
				return;
			}
			var surface = Me.GetSurface(0);
			if (surface == null) {
				return;
			}
			surface.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
			surface.WriteText(text, append: append);
			surface.WriteText(Environment.NewLine, append: true);
		}
	}
}
