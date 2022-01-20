using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Utilities;
using VRageMath;

namespace IngameScript {
	partial class Program : MyGridProgram {
		#region mdk preserve
		private const string JumpDriveName = null;
		private const string RemoteControlName = null;
		private const string InitializeCommand = "initialize";
		private const string AbortCommand = "abort";
		private const string SetDistanceDirectCommand = "jump";
		#endregion

		// todo: add support for an output LCD

		private readonly List<IMyJumpDrive> jumpDrives = new List<IMyJumpDrive>();
		private readonly List<IMyRemoteControl> remoteControls = new List<IMyRemoteControl>();
		private readonly List<IMyGyro> gyros = new List<IMyGyro>();

		private IMyJumpDrive jumpDrive;
		private IMyRemoteControl remoteControl;

		private static readonly UpdateType triggerUpdates = UpdateType.Script | UpdateType.Terminal | UpdateType.Trigger;

		private Vector3D Target;

		private readonly StateMachine stateMachine;

		private bool deadReckoning = false;

		private readonly string _waitingForReadyMessage = $"Waiting{Environment.NewLine}for drives{Environment.NewLine}to be{Environment.NewLine}ready.";
		private readonly string _waitingForOtherDrivesMessage = $"Waiting{Environment.NewLine}for{Environment.NewLine}other drives.";
		private readonly string _readyToJumpMessage = $"Ready{Environment.NewLine}to{Environment.NewLine}jump!";

		public Program() {
			stateMachine = new StateMachine(Runtime);

			Initialize();

			Runtime.UpdateFrequency = UpdateFrequency.None;
		}

		public void Main(string argument, UpdateType updateSource) {
			if ((updateSource & triggerUpdates) != 0) {
				if (argument == InitializeCommand) {
					Initialize();
					return;
				}
				if (argument == AbortCommand) {
					stateMachine.Clear();
					return;
				}
				if (argument.StartsWith(SetDistanceDirectCommand)) {
					int lastSpaceIdx = argument.LastIndexOf(' ');
					if (lastSpaceIdx >= argument.Length) {
						Echo($"Invalid command: \"{argument}\".");
						return;
					}
					var distanceStr = argument.Substring(lastSpaceIdx + 1);
					float distance;
					if (float.TryParse(distanceStr, out distance)) {
						Echo($"Setting direct jump distance of {distance}m.");
						stateMachine.Clear();
						deadReckoning = true;
						targetDistanceM = distance;
						stateMachine.AddSteps(
							SetJumpDistanceAndWait()
							.Concat(DisplayMessage("Ready"))
						);
						stateMachine.RunMachine(runNextTick: true);
						return;
					}
				}
				MyWaypointInfo gps;
				if (GPS.TryParsGpsMaybeWithColor(argument, out gps)) {
					stateMachine.Clear();
					deadReckoning = false;
					Target = gps.Coords;
					stateMachine.AddSteps(
						SetRemoteCoordinates()
						.Concat(AlignShipAndUpdateDistance())
						.Concat(SetJumpDistanceAndWait())
						.Concat(ActivateRemote())
						.Concat(DisplayMessage("Ready"))
					);
				} else {
					Echo($"Invalid argument: \"{argument}\".");
					return;
				}
			}

			stateMachine.RunMachine(runNextTick: true);
		}

		#region initialize
		private void Initialize() {
			Echo("Initializing.");
			if (!FindJumpDrive()) {
				Echo("Could not find a jump drive.");
				return;
			}
			if (!FindRemote()) {
				Echo("Could not find remote.");
				return;
			}
			if (!FindGyros()) {
				Echo("Could not find gyros.");
				return;
			}
			Echo("Found all key blocks.");
		}

		private bool FindGyros() {
			GridTerminalSystem.GetBlocksOfType(gyros);
			return gyros.Count > 0;
		}

		private bool FindJumpDrive() {
			jumpDrive = FindBlock(JumpDriveName, jumpDrives);
			return jumpDrive != null;
		}

		private bool FindRemote() {
			remoteControl = FindBlock(RemoteControlName, remoteControls);
			return remoteControl != null;
		}

		private T FindBlock<T>(string name, List<T> list) where T : class {
			if (name != null) {
				return GridTerminalSystem.GetBlockWithName(name) as T;
			}
			GridTerminalSystem.GetBlocksOfType(list);
			if (list.Count > 0) {
				return list[0];
			}
			return null;
		}
		#endregion

		private IEnumerable<bool> SetRemoteCoordinates() {
			remoteControl.ClearWaypoints();
			remoteControl.AddWaypoint(Target, "NavComp Coordinate");
			remoteControl.FlightMode = FlightMode.OneWay;
			remoteControl.SetCollisionAvoidance(true);
			yield break;
		}

		private float targetDistanceM;

		private IEnumerable<bool> AlignShipAndUpdateDistance() {
			if (deadReckoning) {
				yield break;
			}
			do {
				foreach (var gyro in gyros) {
					gyro.GyroOverride = true;
					SetOrientation(gyro);
				}
				yield return true;
			} while (gyros.Select(gyro => Math.Abs(gyro.Yaw) + Math.Abs(gyro.Pitch) + Math.Abs(gyro.Roll)).Sum() > 0.005);

			foreach (var gyro in gyros) {
				gyro.Roll = 0;
				gyro.Pitch = 0;
				gyro.Yaw = 0;
				gyro.GyroOverride = false;
			}

			yield return true;

			UpdateTargetDistance();

			Echo("Oriented.");
			yield break;
		}

		private IEnumerable<bool> SetJumpDistanceAndWait() {
			bool firstLoop = true;
			while (targetDistanceM > jumpDrive.MinJumpDistanceMeters) {
				// for multi-jump, wait for charging to complete; also waits for a jump in-process
				Echo(_waitingForReadyMessage);
				while (jumpDrive.Status != MyJumpDriveStatus.Ready) {
					yield return true;
				}
				GridTerminalSystem.GetBlocksOfType(jumpDrives);
				Echo(_waitingForOtherDrivesMessage);
				while (jumpDrives.Any(d => d.IsWorking && d.Status != MyJumpDriveStatus.Ready)) {
					yield return true;
				}
				if (deadReckoning && !firstLoop) {
					// update here since updating a jump drive's blind jump distance while it's recharging doesn't work
					UpdateTargetDistance();
				}
				firstLoop = false;
				jumpDrive.JumpDistanceMeters = Math.Min(targetDistanceM, jumpDrive.MaxJumpDistanceMeters);
				Echo(_readyToJumpMessage);
				while (jumpDrive.Status != MyJumpDriveStatus.Jumping) {
					yield return true;
				}
				if (!deadReckoning) {
					// no need to automatically re-orient for dead-reckoning
					// plus, dead-reckoning recalculates desired distance after recharging
					// account for inaccuracies in long trips
					foreach (var _ in AlignShipAndUpdateDistance()) {
						Echo("Aligning.");
						yield return true;
					}
				}
			}
		}

		private IEnumerable<bool> ActivateRemote() {
			remoteControl.SetAutoPilotEnabled(true);
			yield break;
		}

		private IEnumerable<bool> DisplayMessage(string message) {
			Echo(message);
			yield break;
		}

		// from or based on https://github.com/alenoi/SE-Jump-Navigator/blob/master/Jump%20Navigator/Program.cs
		private void UpdateTargetDistance() {
			if (deadReckoning) {
				targetDistanceM -= jumpDrive.MaxJumpDistanceMeters;
			} else {
				targetDistanceM = (float)(remoteControl.GetPosition() - Target).Length();
			}
		}

		public void SetOrientation(IMyGyro gyro) {
			if (gyro.Enabled) {
				Vector3D worldRV;

				Vector3 pos = remoteControl.GetPosition();
				Vector3 target = Target - pos;
				QuaternionD QRV = QuaternionD.CreateFromTwoVectors(target, remoteControl.WorldMatrix.Forward);

				Vector3D axis;
				double angle;
				QRV.GetAxisAngle(out axis, out angle);
				worldRV = axis * Math.Log(1 + round0(angle), 2);

				Vector3D gyroRV = Vector3D.TransformNormal(worldRV, MatrixD.Transpose(gyro.WorldMatrix));

				gyro.Pitch = (float)gyroRV.X;
				gyro.Yaw = (float)gyroRV.Y;
				gyro.Roll = (float)gyroRV.Z;

			}
		}
		private double round0(double d) {
			return Math.Abs(d) < 0.0001 ? 0 : d;
		}
	}
}
