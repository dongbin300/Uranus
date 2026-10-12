using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

using MachineFree.Core;

namespace MachineFree
{
	/// <summary>
	/// 게임 루프와 자동 저장. 실제로 흐른 시간만큼 시뮬레이션을 진행한다.
	/// </summary>
	public sealed class GameSession(Simulation simulation, string savePath)
	{
		private readonly DispatcherTimer tickTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
		private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromSeconds(10) };
		private readonly Stopwatch stopwatch = new();
		private TimeSpan last;

		public Simulation Simulation { get; } = simulation;

		public string SavePath { get; } = savePath;

		/// <summary>
		/// 시뮬레이션을 진행한 직후 (UI 스레드)
		/// </summary>
		public event Action? Ticked;

		/// <summary>
		/// 저장에 실패했을 때 (UI 스레드)
		/// </summary>
		public event Action<Exception>? SaveFailed;

		public void Start()
		{
			tickTimer.Tick += (_, _) => Tick();
			saveTimer.Tick += (_, _) => Save();
			stopwatch.Start();
			tickTimer.Start();
			saveTimer.Start();
		}

		public void Stop()
		{
			tickTimer.Stop();
			saveTimer.Stop();
			Tick();
			Save();
		}

		public void Save()
		{
			try
			{
				Simulation.State.SavedAt = DateTimeOffset.UtcNow;
				SaveFile.Write(SavePath, Simulation.State);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
			{
				SaveFailed?.Invoke(e);
			}
		}

		private void Tick()
		{
			var now = stopwatch.Elapsed;
			var seconds = (now - last).TotalSeconds;
			last = now;

			// 절전 등으로 오래 멈췄다 돌아온 경우만 오프라인 진행처럼 상한을 둔다
			if (seconds > LongPauseSeconds)
			{
				var cap = Simulation.Database.Config.OfflineCapHours;
				seconds = Math.Min(seconds, double.IsFinite(cap) && cap > 0 ? cap * 3600 : 0);
			}

			Simulation.Tick(seconds);
			Ticked?.Invoke();
		}

		private const double LongPauseSeconds = 60;
	}
}
