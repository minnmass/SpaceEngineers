using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Utilities {
	public class StateMachine {
		private IEnumerator<bool> _machine;
		private IEnumerable<bool> _nextMachine;

		private readonly IMyGridProgramRuntimeInfo _runtime;

		public StateMachine(IMyGridProgramRuntimeInfo runtime) {
			_runtime = runtime;
		}

		/// <summary>
		/// Run the next "step" in the machine.
		/// </summary>
		/// <param name="runNextTick">If true, will set update frequency to "once" to run on the next tick</param>
		/// <returns>true if a step is taken</returns>
		public bool RunMachine(bool runNextTick = true) {
			if (_machine == null) {
				if (_nextMachine == null) {
					return false;
				}
				_machine = _nextMachine.GetEnumerator();
				_nextMachine = null;
			}
			if (_machine.MoveNext()) {
				if (runNextTick) {
					_runtime.UpdateFrequency |= UpdateFrequency.Once;
				}
				return true;
			}
			_machine.Dispose();
			_machine = null;
			return false;
		}

		public void AddSteps(IEnumerable<bool> steps) {
			if (_machine == null) {
				_machine = steps.GetEnumerator();
			} else if (_nextMachine == null) {
				_nextMachine = steps;
			} else {
				_nextMachine = _nextMachine.Concat(steps);
			}
		}

		public void Clear() {
			_nextMachine = null;
			if (_machine != null) {
				_machine.Dispose();
				_machine = null;
			}
		}
	}
}
