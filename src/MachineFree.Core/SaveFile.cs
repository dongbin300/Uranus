using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Uranus;

namespace MachineFree.Core
{
	/// <summary>
	/// 세이브 파일(JSON). 아이템과 레시피는 ID로만 저장해서 데이터를 고쳐도 세이브가 깨지지 않는다.
	/// v1(예전 GameManager의 Save.json)은 읽을 때 자동으로 변환한다.
	/// </summary>
	public static class SaveFile
	{
		public const int CurrentVersion = 2;

		private static readonly JsonSerializerOptions Options = new()
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		};

		/// <summary>
		/// %LOCALAPPDATA%\MachineFree\save.json
		/// </summary>
		public static string DefaultPath { get; } = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MachineFree", "save.json");

		public static string Serialize(GameState state)
		{
			var dto = new SaveDto
			{
				Version = CurrentVersion,
				SavedAt = state.SavedAt,
				PlayTime = state.PlayTime,
				TotalCrafts = state.TotalCrafts,
				Inventory = state.Inventory.ToDictionary(x => x.Key.Value, x => x.Value),
				Discovered = [.. state.Discovered.Select(x => x.Value).Order(StringComparer.Ordinal)],
				Queue = [.. state.Queue.Select(x => new QueueDto
				{
					Recipe = x.Recipe.Value,
					Remaining = x.Remaining,
					Progress = x.Progress,
					Paid = x.Paid.ToDictionary(p => p.Key.Value, p => p.Value),
				})],
			};

			return JsonSerializer.Serialize(dto, Options);
		}

		/// <summary>
		/// JSON을 읽는다. v1이면 아이템 이름으로 ID를 찾아 변환하고, 찾지 못한 이름은 warnings에 담는다.
		/// </summary>
		public static GameState Deserialize(string json, UranusDatabase db, out IReadOnlyList<string> warnings)
		{
			using var document = JsonDocument.Parse(json);
			if (document.RootElement.TryGetProperty("InventoryItems", out _))
			{
				return LegacySave.Import(document.RootElement, db, out warnings);
			}

			warnings = [];
			var dto = document.RootElement.Deserialize<SaveDto>(Options) ?? throw new JsonException("세이브가 비어 있다");
			if (dto.Version > CurrentVersion)
			{
				throw new JsonException($"더 새로운 버전({dto.Version})의 세이브다");
			}

			var state = new GameState
			{
				SavedAt = dto.SavedAt,
				PlayTime = dto.PlayTime,
				TotalCrafts = dto.TotalCrafts,
			};

			foreach (var (id, amount) in dto.Inventory)
			{
				if (amount > 0 && double.IsFinite(amount))
				{
					state.Inventory[new ItemId(id)] = amount;
				}
			}

			state.Discovered.UnionWith(dto.Discovered.Select(x => new ItemId(x)));
			foreach (var x in dto.Queue.Where(x => x.Remaining > 0))
			{
				var entry = new QueueEntry { Recipe = new RecipeId(x.Recipe), Remaining = x.Remaining, Progress = Math.Max(0, x.Progress) };
				foreach (var (id, amount) in x.Paid)
				{
					entry.Paid[new ItemId(id)] = amount;
				}

				state.Queue.Add(entry);
			}

			return state;
		}

		/// <summary>
		/// 임시 파일에 쓴 뒤 바꿔치기한다. 이전 세이브는 .bak으로 남는다.
		/// </summary>
		public static void Write(string path, GameState state)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
			var temp = path + ".tmp";
			File.WriteAllText(temp, Serialize(state), new UTF8Encoding(false));
			if (File.Exists(path))
			{
				File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
			}
			else
			{
				File.Move(temp, path);
			}
		}

		/// <summary>
		/// 세이브를 읽는다. 본 파일이 깨졌으면 .bak을 시도한다. 둘 다 없으면 null.
		/// </summary>
		public static GameState? TryRead(string path, UranusDatabase db, out IReadOnlyList<string> warnings)
		{
			warnings = [];
			foreach (var candidate in new[] { path, path + ".bak" })
			{
				if (!File.Exists(candidate))
				{
					continue;
				}

				try
				{
					var state = Deserialize(File.ReadAllText(candidate), db, out warnings);
					if (candidate != path)
					{
						warnings = [.. warnings, $"세이브가 손상되어 백업({Path.GetFileName(candidate)})에서 불러왔다"];
					}

					return state;
				}
				catch (JsonException)
				{
				}
			}

			return null;
		}

		private sealed class SaveDto
		{
			public int Version { get; set; }
			public DateTimeOffset SavedAt { get; set; }
			public double PlayTime { get; set; }
			public long TotalCrafts { get; set; }
			public Dictionary<string, double> Inventory { get; set; } = [];
			public List<string> Discovered { get; set; } = [];
			public List<QueueDto> Queue { get; set; } = [];
		}

		private sealed class QueueDto
		{
			public string Recipe { get; set; } = "";
			public long Remaining { get; set; }
			public double Progress { get; set; }
			public Dictionary<string, double> Paid { get; set; } = [];
		}
	}

	/// <summary>
	/// 예전(v1) 세이브 변환. 아이템은 한국어 이름으로 찾고, 대기열은 재료로 환불한다.
	/// </summary>
	public static class LegacySave
	{
		public static GameState Import(JsonElement root, UranusDatabase db, out IReadOnlyList<string> warnings)
		{
			var problems = new List<string>();
			var state = new GameState { SavedAt = DateTimeOffset.UtcNow };

			foreach (var entry in ArrayItems(root, "InventoryItems"))
			{
				Add(entry, 1);
			}

			foreach (var queued in ArrayItems(root, "QueueItems"))
			{
				var count = Number(queued, "Count");
				if (queued.TryGetProperty("Recipe", out var recipe))
				{
					foreach (var material in ArrayItems(recipe, "Materials"))
					{
						Add(material, count);
					}
				}
			}

			state.PlayTime = Number(root, "ElapsedTime");
			state.TotalCrafts = (long)Number(root, "ProduceCount");
			warnings = problems;
			return state;

			void Add(JsonElement stack, double multiplier)
			{
				var name = stack.TryGetProperty("Item", out var item) && item.TryGetProperty("KoreanName", out var n) ? n.GetString() : null;
				var amount = Number(stack, "Count") * multiplier;
				if (name is null || !(amount > 0))
				{
					return;
				}

				if (db.FindItemByName(name) is { } def)
				{
					state.Add(def.Id, amount);
				}
				else
				{
					problems.Add($"예전 세이브의 '{name}'에 해당하는 아이템이 없어 버렸다");
				}
			}
		}

		private static IEnumerable<JsonElement> ArrayItems(JsonElement element, string name) =>
			element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

		private static double Number(JsonElement element, string name) =>
			element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
	}
}
