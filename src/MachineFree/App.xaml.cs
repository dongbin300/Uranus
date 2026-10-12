using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using MachineFree.Core;
using MachineFree.ViewModels;

using Uranus;
using Uranus.Data;
using Uranus.Validation;

namespace MachineFree
{
	public partial class App : Application
	{
		private GameSession? session;

		private static string CrashLogPath => Path.Combine(Path.GetDirectoryName(SaveFile.DefaultPath)!, "crash.log");

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			DispatcherUnhandledException += OnUnhandledException;

#if DEBUG
			// MACHINEFREE_BINDING_LOG=<파일>: 바인딩 오류를 파일에 남긴다
			if (Environment.GetEnvironmentVariable("MACHINEFREE_BINDING_LOG") is { Length: > 0 } bindingLog)
			{
				PresentationTraceSources.Refresh();
				PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(bindingLog));
				PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
				Trace.AutoFlush = true;
			}
#endif

			if (LoadDatabase() is not { } db)
			{
				Shutdown(1);
				return;
			}

			// --save <경로>: 다른 세이브 파일로 실행 (테스트용)
			var savePath = e.Args.SkipWhile(x => x != "--save").Skip(1).FirstOrDefault() ?? SaveFile.DefaultPath;

			var warnings = new List<string>();
			if (LoadState(savePath, db, warnings) is not { } state)
			{
				Shutdown(1);
				return;
			}

			var simulation = new Simulation(db, state);
			var offline = simulation.CatchUp(DateTimeOffset.UtcNow);

			session = new GameSession(simulation, savePath);
			var viewModel = new MainViewModel(simulation);
			viewModel.ShowOfflineReport(offline, warnings);
			session.Ticked += viewModel.Refresh;
			session.SaveFailed += x => viewModel.StatusText = $"저장 실패: {x.Message}";

			var window = new MainWindow { DataContext = viewModel };
			window.Closing += (_, _) => session.Stop();
			MainWindow = window;
			window.Show();

			session.Save();
			session.Start();

			// --screenshot <경로> [--screenshot-delay 초]: 창을 PNG로 저장하고 닫는다 (README·테스트용)
			if (e.Args.SkipWhile(x => x != "--screenshot").Skip(1).FirstOrDefault() is { } shotPath)
			{
				var delay = double.TryParse(e.Args.SkipWhile(x => x != "--screenshot-delay").Skip(1).FirstOrDefault(), out var seconds) ? seconds : 2;
				var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
				timer.Tick += (_, _) =>
				{
					timer.Stop();
					SaveScreenshot(window, shotPath);
					window.Close();
				};
				timer.Start();
			}
		}

		private static void SaveScreenshot(Window window, string path)
		{
			var content = (FrameworkElement)window.Content;
			var dpi = VisualTreeHelper.GetDpi(window);
			var bitmap = new RenderTargetBitmap(
				(int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
				(int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY),
				dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
			bitmap.Render(content);

			var encoder = new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(bitmap));
			using var stream = File.Create(path);
			encoder.Save(stream);
		}

		/// <summary>
		/// 데이터를 읽고 검증한다. 오류가 있으면 알리고 null.
		/// </summary>
		private static UranusDatabase? LoadDatabase()
		{
			var directory = UranusDataLoader.FindDataDirectory(AppContext.BaseDirectory);
			if (directory is null)
			{
				MessageBox.Show("data 폴더를 찾지 못했습니다.", "MACHINE FREE", MessageBoxButton.OK, MessageBoxImage.Error);
				return null;
			}

			var result = UranusDataLoader.Load(directory);
			var errors = result.Issues.Concat(UranusValidator.Validate(result.Database)).Where(x => x.Severity == IssueSeverity.Error).ToList();
			if (errors.Count > 0)
			{
				var message = string.Join(Environment.NewLine, errors.Take(15).Select(x => x.ToString()));
				MessageBox.Show($"데이터 오류 {errors.Count}개{Environment.NewLine}{Environment.NewLine}{message}", "MACHINE FREE", MessageBoxButton.OK, MessageBoxImage.Error);
				return null;
			}

			return result.Database;
		}

		/// <summary>
		/// 세이브를 읽는다. 읽을 수 없는 세이브는 새 게임으로 덮어쓰지 않도록 옆으로 옮긴다.
		/// 옮기지도 못하면 알리고 null(종료).
		/// </summary>
		private static GameState? LoadState(string savePath, UranusDatabase db, List<string> warnings)
		{
			var result = SaveFile.Read(savePath, db);
			warnings.AddRange(result.Warnings);
			if (result.Status == SaveReadStatus.Loaded)
			{
				return result.State;
			}

			if (result.Status == SaveReadStatus.Unreadable)
			{
				try
				{
					var moved = SaveFile.MoveAside(savePath, DateTimeOffset.Now);
					warnings.Add($"세이브를 읽을 수 없어 새 게임으로 시작했습니다({result.Error}). 원래 파일은 옮겨 두었습니다: {string.Join(", ", moved.Select(Path.GetFileName))}");
				}
				catch (Exception e) when (e is IOException or UnauthorizedAccessException)
				{
					MessageBox.Show($"세이브를 읽을 수 없고 옮기지도 못했습니다. 파일을 지키려고 종료합니다.{Environment.NewLine}{result.Error}{Environment.NewLine}{e.Message}",
						"MACHINE FREE", MessageBoxButton.OK, MessageBoxImage.Error);
					return null;
				}
			}

			return ImportLegacySave(db, warnings) ?? GameState.CreateNew(db);
		}

		/// <summary>
		/// 예전 버전이 실행 폴더에 남긴 Dictionary/Save.json이 있으면 변환한다.
		/// </summary>
		private static GameState? ImportLegacySave(UranusDatabase db, List<string> warnings)
		{
			var path = Path.Combine(AppContext.BaseDirectory, "Dictionary", "Save.json");
			if (!File.Exists(path))
			{
				return null;
			}

			var result = SaveFile.Read(path, db);
			if (result.State is null)
			{
				warnings.Add($"예전 세이브를 읽지 못했습니다({result.Error}).");
				return null;
			}

			warnings.Add("예전 세이브를 새 형식으로 옮겼습니다.");
			warnings.AddRange(result.Warnings);
			return result.State;
		}

		private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
				File.AppendAllText(CrashLogPath, $"[{DateTimeOffset.Now:O}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
				session?.Save();
			}
			catch (IOException)
			{
			}

			MessageBox.Show($"예상하지 못한 오류가 났습니다. 진행 상황은 저장했습니다.{Environment.NewLine}{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}로그: {CrashLogPath}",
				"MACHINE FREE", MessageBoxButton.OK, MessageBoxImage.Warning);
			e.Handled = true;
		}
	}
}
