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

		private readonly Logger logger;
		private readonly StateMachine stateMachine;

		private bool deadReckoning = false;

		public Program() {
			logger = new Logger(Me);
			stateMachine = new StateMachine(Runtime);

			Initialize();

			Runtime.UpdateFrequency = UpdateFrequency.None;
		}

		public void Main(string argument, UpdateType updateSource) {
			logger.Log("Processing...", append: false);
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
					if (lastSpaceIdx <= argument.Length) {
						logger.Log($"Invalid command: \"{argument}\".");
						return;
					}
					var distanceStr = argument.Substring(lastSpaceIdx + 1);
					float distance;
					if (float.TryParse(distanceStr, out distance)) {
						logger.Log($"Setting direct jump distance of {distance}m.");
						stateMachine.Clear();
						deadReckoning = true;
						targetDistanceM = distance;
						stateMachine.AddSteps(
							SetJumpDistanceAndWait()
							.Concat(DisplayMessage("Jump(s) complete."))
						);
					}
				}
				MyWaypointInfo gps;
				if (GPS.TryParsGpsMaybeWithColor(argument, out gps)) {
					stateMachine.Clear();
					deadReckoning = false;
					logger.Log("Accepted coordinates.");
					Target = gps.Coords;
					stateMachine.AddSteps(
						SetRemoteCoordinates()
						.Concat(AlignShip())
						.Concat(SetJumpDistanceAndWait())
						.Concat(ActivateRemote())
					);
				} else {
					logger.Log($"Invalid argument: \"{argument}\".");
					return;
				}
			}

			stateMachine.RunMachine(runNextTick: true);
		}

		#region initialize
		private void Initialize() {
			logger.Log("Initializing.", append: false);
			if (!FindJumpDrive()) {
				logger.Log("Could not find a jump drive.");
				return;
			}
			if (!FindRemote()) {
				logger.Log("Could not find remote.");
				return;
			}
			if (!FindGyros()) {
				logger.Log("Could not find gyros.");
				return;
			}
			logger.Log("Found all key blocks.");
		}

		private bool FindGyros() {
			logger.Log("Finding gyros");
			GridTerminalSystem.GetBlocksOfType(gyros);
			return gyros.Count > 0;
		}

		private bool FindJumpDrive() {
			logger.Log("Finding jump drive");
			jumpDrive = FindBlock(JumpDriveName, jumpDrives);
			return jumpDrive != null;
		}

		private bool FindRemote() {
			logger.Log("Finding remote");
			remoteControl = FindBlock(RemoteControlName, remoteControls);
			return remoteControl != null;
		}

		private T FindBlock<T>(string name, List<T> list) where T : class {
			if (name != null) {
				logger.Log($"Looking for block named {name}.");
				return GridTerminalSystem.GetBlockWithName(name) as T;
			}
			logger.Log("Looking for the first instance.");
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

		private IEnumerable<bool> AlignShip() {
			SetTargetDistance();
			if (targetDistanceM < jumpDrive.MinJumpDistanceMeters) {
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

			logger.Log("Oriented.");
			yield break;
		}

		private IEnumerable<bool> SetJumpDistanceAndWait() {
			while (targetDistanceM > jumpDrive.MinJumpDistanceMeters) {
				// for multi-jump, wait for charging to complete; also waits for a jump in-process
				while (jumpDrive.Status != MyJumpDriveStatus.Ready) {
					yield return true;
				}
				string jumpReadyMessage = $"Ready to jump {targetDistanceM} meters with jump drive \"{jumpDrive.DisplayName}\".";
				logger.Log(jumpReadyMessage);
				Echo(jumpReadyMessage);
				jumpDrive.JumpDistanceMeters = Math.Min(targetDistanceM, jumpDrive.MaxJumpDistanceMeters);
				while (jumpDrive.Status != MyJumpDriveStatus.Jumping) {
					yield return true;
				}
				SetTargetDistance();
			}
		}

		private IEnumerable<bool> ActivateRemote() {
			remoteControl.SetAutoPilotEnabled(true);
			logger.Log("Autopilot activated. Enjoy your flight.");
			yield break;
		}

		private IEnumerable<bool> DisplayMessage(string message) {
			logger.Log(message);
			yield break;
		}

		// from or based on https://github.com/alenoi/SE-Jump-Navigator/blob/master/Jump%20Navigator/Program.cs
		private void SetTargetDistance() {
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
