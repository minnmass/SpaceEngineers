using Sandbox.ModAPI.Ingame;
using System;

namespace Utilities {
	public static class GPS {
		// GPS:Drone Repair Platform:-4686:2489:-12959:#FF00FF:
		// GPS:<name>:<x>:<y>:<z>:<color; optional>:
		// only one trailing colon regardless of whether a color is present
		private static readonly char[] gpsSplitChars = new[] { ':' };
		public static bool TryParsGpsMaybeWithColor(string text, out MyWaypointInfo gps) {
			if (String.IsNullOrWhiteSpace(text)) {
				gps = default(MyWaypointInfo);
				return false;
			}

			var chunks = text.Split(gpsSplitChars, StringSplitOptions.RemoveEmptyEntries);
			if (chunks.Length < 5) {
				gps = default(MyWaypointInfo);
				return false;
			}

			string name = chunks[1];
			double x, y, z;
			if (
				double.TryParse(chunks[2], out x)
				&& double.TryParse(chunks[3], out y)
				&& double.TryParse(chunks[4], out z)
			) {
				gps = new MyWaypointInfo(name, x, y, z);
				return true;
			}

			gps = default(MyWaypointInfo);
			return false;
		}
	}
}
