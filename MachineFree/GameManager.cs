using MachineFree.Serialization;

using Newtonsoft.Json;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using Uranus;
using Uranus.Dictionary;
using Uranus.Extensions;

namespace MachineFree
{
	public class GameManager
	{
		public class SaveData
		{
			public required ObservableCollection<UranusInventoryItem> InventoryItems { get; set; }
			public required ObservableCollection<UranusQueueItem> QueueItems { get; set; }
			public decimal ElapsedTime { get; set; }
			public decimal ProduceCount { get; set; }
		}

		/// <summary>
		/// Queue for production
		/// </summary>
		public static MfQueue Queue { get; set; } = new();

		/// <summary>
		/// Item list of Uranus dictionary
		/// </summary>
		public static ObservableCollection<UranusItem> UranusItems = [];

		/// <summary>
		/// Recipe list of Uranus dictionary
		/// </summary>
		public static ObservableCollection<UranusRecipe> UranusRecipes = [];
		public static ObservableCollection<UranusRecipe> UranusGenerators = [];

		/// <summary>
		/// Inventory of Machine Free
		/// </summary>
		public static UranusInventory Inventory = new();

		public static decimal TimeElapsePerOneFrame = 0.1m;

		public static void Init()
		{
			UranusDictionaryManager.ConvertDictionaryToCsv();
			UranusDictionaryManager.LoadUranusDictionary();

			UranusItems.Clear();
			foreach (var item in UranusDictionaryManager.Items)
			{
				UranusItems.Add(item);
			}

			UranusRecipes.Clear();
			foreach (var recipe in UranusDictionaryManager.Recipes)
			{
				UranusRecipes.Add(recipe);
			}

			UranusGenerators.Clear();
			foreach (var generator in UranusDictionaryManager.Generators)
			{
				UranusGenerators.Add(generator);
			}

			Load();

			// Start pack
			if (Statistics.ElapsedTime < 1)
			{
				Inventory.Plus(UranusDictionaryManager.GetItem("수소"), 15);
			}
		}

		/// <summary>
		/// Save game status to file
		/// </summary>
		public static void Save()
		{
			var data = new SaveData
			{
				InventoryItems = Inventory.Items,
				QueueItems = Queue.Items,
				ElapsedTime = Statistics.ElapsedTime,
				ProduceCount = Statistics.ProduceCount
			};

			var json = JsonConvert.SerializeObject(data, Formatting.Indented, new JsonSerializerSettings { ContractResolver = new IgnoreReadOnlyResolver() });
			File.WriteAllText(UranusDictionaryManager.SaveFilePath, json);
		}

		/// <summary>
		/// Load game status from file
		/// </summary>
		public static void Load()
		{
			var jsonData = File.ReadAllText(UranusDictionaryManager.SaveFilePath);
			var data = JsonConvert.DeserializeObject<SaveData>(jsonData);

			if (data == null)
			{
				return;
			}

			Inventory.Items.Clear();
			foreach (var item in data.InventoryItems)
			{
				Inventory.Items.Add(item);
			}
			Queue.Items.Clear();
			foreach (var item in data.QueueItems)
			{
				Queue.Items.Add(item);
			}
			Statistics.ElapsedTime = data.ElapsedTime;
			Statistics.ProduceCount = data.ProduceCount;
		}

		public static bool IsProducable(UranusRecipe recipe)
		{
			return GetProducableCount(recipe) > 0;
		}

		public static decimal GetProducableCount(UranusRecipe recipe)
		{
			var counts = new List<int>();
			foreach (var material in recipe.Materials)
			{
				var item = Inventory.Items.FirstOrDefault(x => x.Item.Equals(material.Item));

				if (item == null)
				{
					return 0;
				}

				var count = (int)(item.Count / material.Count);
				counts.Add(count);
			}

			if (counts.Count == 0)
			{
				return 0;
			}

			return counts.Min();
		}

		public static void Produce(UranusRecipe recipe, decimal count = 1)
		{
			foreach (var material in recipe.Materials)
			{
				Inventory.Minus(material.Item, material.Count * count);
			}

			var queueItem = new UranusQueueItem(recipe, count);

			Queue.Enqueue(queueItem);
		}

		public static void Revert(UranusQueueItem queueItem, decimal count = 1)
		{
			foreach (var material in queueItem.Recipe.Materials)
			{
				Inventory.Plus(material.Item, material.Count * count);
			}

			Queue.Remove(queueItem, count);
		}

		/// <summary>
		/// It should be called every frame
		/// </summary>
		public static void Process()
		{
			Statistics.ElapsedTime += TimeElapsePerOneFrame;

			// Calculate producable count of recipe
			// 이거 추후 바인딩해야함
			foreach (var recipe in UranusRecipes)
			{
				recipe.ProducableCount = GetProducableCount(recipe);
				recipe.FormatProducableCount = recipe.ProducableCount.ToFormatLargeNumber();
			}

			// Calculate producable count of generator
			// 이거 추후 바인딩해야함
			foreach (var recipe in UranusGenerators)
			{
				recipe.ProducableCount = GetProducableCount(recipe);
				recipe.FormatProducableCount = recipe.ProducableCount.ToFormatLargeNumber();
			}

			// Calculate item count of inventory
			//foreach (var item in Inventory.Items)
			//{
			//	item.FormatCount = item.Count.ToFormatLargeNumber();
			//}

			if (Queue.CurrentItem == null)
			{
				return;
			}

			// Increase time of current queue
			Queue.CurrentItem.TimeElapsed += TimeElapsePerOneFrame;

			// Complete one item of a recipe
			if (Queue.CurrentItem.TimeElapsed >= Queue.CurrentItem.Recipe.Time)
			{
				Queue.CurrentItem.TimeElapsed -= Queue.CurrentItem.Recipe.Time;
				Queue.CurrentItem.Count--;
				foreach (var product in Queue.CurrentItem.Recipe.Products)
				{
					Inventory.Plus(product.Item, product.Count);
				}
				Statistics.ProduceCount++;
			}

			// Complete all item of a recipe
			if (Queue.CurrentItem.Count <= 0)
			{
				Queue.Dequeue();
			}
		}

		/// <summary>
		/// It should be called every second
		/// </summary>
		public static void Process1s()
		{
			// 생성기에서 얻는 생성품 처리
			foreach (var recipe in UranusGenerators)
			{
				if (Inventory.Has(recipe.Products[0].Item))
				{
					foreach (var generationItem in recipe.Generations)
					{
						Inventory.Plus(generationItem.Item, generationItem.Count);
					}
				}
			}
		}
	}
}
