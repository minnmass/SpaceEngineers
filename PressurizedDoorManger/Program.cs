using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IngameScript {
	partial class Program : MyGridProgram {
		public Program() {
			Runtime.UpdateFrequency = UpdateFrequency.None;
			Log("Starting.", append: false);
		}

		private readonly LinkedList<StateMachine> _stateMachines = new LinkedList<StateMachine>();

		public void Main(string argument, UpdateType updateSource) {
			if ((updateSource & (UpdateType.Trigger | UpdateType.Terminal)) > 0) {
				if (argument == "status") {
					Log($"Monitoring {_stateMachines.Count} machines.", append: false);
					return;
				}
				_stateMachines.AddLast(new StateMachine(argument, this));
			}
			RunStateMachine();
		}

		private void Log(string text, bool append = true) {
			var surface = Me.GetSurface(0);
			surface.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
			surface.WriteText(text, append: append);
			surface.WriteText(Environment.NewLine, append: true);
		}

		public void RunStateMachine() {
			bool ranSteps = false;
			var current = _stateMachines?.First;
			if (current == null) {
				return;
			}

			while (current != null) {
				var next = current.Next;
				if (current.Value.MoveNext()) {
					ranSteps = true;
				}
				if (current.Value.Done) {
					Log($"Done with {current.Value}.");
					_stateMachines.Remove(current);
				}
				current = next;
			}
			if (ranSteps) {
				Runtime.UpdateFrequency |= UpdateFrequency.Once;
			}
		}

		private class StateMachine {
			private static int _counter = 0;
			public readonly int Id;
			const float minimumPressurizationDelta = 0.000001F;

			public readonly string DoorGroup;
			public readonly string VentGroup;
			public readonly string InitString;

			public readonly List<IMyAirVent> Vents;
			private readonly List<float> _ventPressures;
			public readonly List<IMyDoor> Doors;

			private readonly DateTime endBy;

			private IEnumerator<bool> _machine;

			private readonly Program Program;

			public bool MoveNext() {
				if (Done || _machine == null) {
					return false;
				}
				if (_machine.MoveNext()) {
					return true;
				}
				Done = true;
				_machine.Dispose();
				_machine = null;
				return false;
			}

			public bool Done { get; private set; }

			public StateMachine(string init, Program program) {
				Id = ++_counter;
				InitString = init;
				Program = program;
				endBy = DateTime.UtcNow.AddSeconds(10);

				const char delim = '|';
				var delimCount = init.Count(c => c == delim);
				if (delimCount > 1) {
					Done = true;
					Program.Echo("Returning due to too many delims.");
					return;
				}
				Done = false;
				if (delimCount == 1) {
					var split = init.Split(delim);
					DoorGroup = split[0].Trim();
					VentGroup = split[1].Trim();
				} else {
					DoorGroup = init.Trim();
					VentGroup = null;
				}

				var terminal = Program.GridTerminalSystem;
				Vents = InitArray<IMyAirVent>(terminal, VentGroup);
				Doors = InitArray<IMyDoor>(terminal, DoorGroup);

				Program.Log($"Found {Vents.Count} vents in group {VentGroup} and {Doors.Count} doors in group {DoorGroup}.");

				_ventPressures = new List<float>();
				foreach (var vent in Vents) {
					_ventPressures.Add(vent.GetOxygenLevel());
				}
				Program.Log($"Found {_ventPressures.Count} pressures for {Vents.Count} vents.");

				_machine = Doors.Any(d => d.Status == DoorStatus.Open || d.Status == DoorStatus.Opening)
					? SetDoorsStatus(toClosed: true)
						.Concat(SetVentsStatus(toPressurize: true))
						.GetEnumerator()
					: SetVentsStatus(toPressurize: false)
						.Concat(WaitForDepressurization())
						.Concat(SetDoorsStatus(toClosed: false))
						.GetEnumerator();
			}

			public override string ToString() {
				return $"{InitString} -- {Id}";
			}

			private List<T> InitArray<T>(IMyGridTerminalSystem terminal, string groupName) where T : class {
				var result = new List<T>();
				if (String.IsNullOrWhiteSpace(groupName)) {
					terminal.GetBlocksOfType(result);
				} else {
					terminal.GetBlockGroupWithName(groupName).GetBlocksOfType(result);
				}
				return result;
			}

			private IEnumerable<bool> SetDoorsStatus(bool toClosed) {
				if (toClosed) {
					Program.Log($"Closing doors in group {DoorGroup}.");
					foreach (var door in Doors) {
						door.CloseDoor();
					}
				} else {
					Program.Log($"Opening doors in group {DoorGroup}.");
					foreach (var door in Doors) {
						door.OpenDoor();
					}
				}
				if (toClosed) {
					while (Doors.Any(d => d.Status != DoorStatus.Closed)) {
						yield return true;
					}
				}
			}

			private IEnumerable<bool> WaitForDepressurization() {
				Program.Log($"Examining {_ventPressures.Count} pressures for {Vents.Count} vents.");
				while (Vents.Any(v => !v.Depressurize)) {
					Program.Log("Waiting for vents to start depressurizing.");
					yield return true;
				}
				// delay to kickstart depressurization
				for (int i = 0; i < 30; ++i) {
					yield return true;
				}
				for (int i = 0; i < Vents.Count; ++i) {
					var ventPressure = Vents[i].GetOxygenLevel();
					while (DateTime.UtcNow < endBy && ventPressure > 0 && Math.Abs(_ventPressures[i] - ventPressure) > minimumPressurizationDelta) {
						Program.Log($"Depressurizing {VentGroup}.");
						yield return true;
					}
					Program.Log($"Done depressurizing vent {i}.");
				}
			}

			private IEnumerable<bool> SetVentsStatus(bool toPressurize) {
				foreach (var vent in Vents) {
					vent.Depressurize = !toPressurize;
				}
				yield return true;
			}
		}
	}
}
