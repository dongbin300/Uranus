using Newtonsoft.Json;

using System.Collections.ObjectModel;
using System.ComponentModel;

using Uranus.Interfaces;

namespace Uranus
{
	public class UranusRecipe : IBindable, IEquatable<UranusRecipe>
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		public void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private string name = string.Empty;
		public string Name
		{
			get => name;
			set
			{
				if (name != value)
				{
					name = value;
					OnPropertyChanged(nameof(Name));
				}
			}
		}

		private ObservableCollection<UranusMaterial> materials = [];
		public ObservableCollection<UranusMaterial> Materials
		{
			get => materials;
			set
			{
				if (materials != value)
				{
					materials = value;
					OnPropertyChanged(nameof(Materials));
				}
			}
		}

		private ObservableCollection<UranusProduct> products = [];
		public ObservableCollection<UranusProduct> Products
		{
			get => products;
			set
			{
				if (products != value)
				{
					products = value;
					OnPropertyChanged(nameof(Products));
				}
			}
		}

		private ObservableCollection<UranusGeneration> generations = [];
		public ObservableCollection<UranusGeneration> Generations
		{
			get => generations;
			set
			{
				if (generations != value)
				{
					generations = value;
					OnPropertyChanged(nameof(Generations));
				}
			}
		}

		private decimal time = 0m;
		public decimal Time
		{
			get => time;
			set
			{
				if (time != value)
				{
					time = value;
					OnPropertyChanged(nameof(Time));
				}
			}
		}

		private decimal producableCount = 0m;
		[JsonIgnore]
		public decimal ProducableCount
		{
			get => producableCount;
			set
			{
				if (producableCount != value)
				{
					producableCount = value;
					OnPropertyChanged(nameof(ProducableCount));
				}
			}
		}

		private string formatProducableCount = "0";
		[JsonIgnore]
		public string FormatProducableCount
		{
			get => formatProducableCount;
			set
			{
				if (formatProducableCount != value)
				{
					formatProducableCount = value;
					OnPropertyChanged(nameof(FormatProducableCount));
				}
			}
		}

		public string TopString => Products[0].Item.ToString();
		public string TitleString => $"{string.Join(", ", Products.Select(x => x.Item))} 제작";
		public string TimeString => $"{Time:G} 초";
		public string ProductsString => $"{string.Join(Environment.NewLine, Products)}";
		public string MaterialsString => $"{string.Join(Environment.NewLine, Materials)}";

		/// <summary>
		/// 생산품 종류 개수
		/// </summary>
		public static readonly int ProductsColumnCount = 4;

		/// <summary>
		/// 재료 종류 개수
		/// </summary>
		public static readonly int MaterialsColumnCount = 17;

		/// <summary>
		/// 생성품 종류 개수
		/// </summary>
		public static readonly int GeneratesColumnCount = 4;

		public UranusRecipe()
		{
			name = string.Empty;
			materials = [];
			products = [];
			generations = [];
			time = 0m;
		}

		[JsonConstructor]
		public UranusRecipe(string name, ObservableCollection<UranusMaterial> materials, ObservableCollection<UranusProduct> products, ObservableCollection<UranusGeneration> generations, decimal time)
		{
			this.name = name;
			this.materials = materials;
			this.products = products;
			this.generations = generations;
			this.time = time;
		}

		public UranusRecipe(string recipeString)
		{
			var segment = recipeString.Split(',');

			// Products
			for (int i = 0; i < ProductsColumnCount * 2; i += 2)
			{
				if (string.IsNullOrEmpty(segment[i]))
				{
					break;
				}

				// count format
				// "2" => 2
				// "1.66^24" => 1.66*10^24
				var nSegment = segment[i + 1].Split('^');
				var count = nSegment.Length == 1 ?
					decimal.Parse(segment[i + 1]) :
					(decimal)(double.Parse(nSegment[0]) * Math.Pow(10, double.Parse(nSegment[1])));

				Products.Add(new UranusProduct(new UranusItem(segment[i].Trim()), count));
			}

			// Time
			Time = string.IsNullOrEmpty(segment[ProductsColumnCount * 2]) ?
				0 :
				decimal.Parse(segment[ProductsColumnCount * 2]);

			// Materials
			for (int i = ProductsColumnCount * 2 + 1; i < ProductsColumnCount * 2 + 1 + MaterialsColumnCount * 2; i += 2)
			{
				if (string.IsNullOrEmpty(segment[i]))
				{
					break;
				}

				var nSegment = segment[i + 1].Split('^');
				var count = nSegment.Length == 1 ?
					decimal.Parse(segment[i + 1]) :
					(decimal)(double.Parse(nSegment[0]) * Math.Pow(10, double.Parse(nSegment[1])));

				Materials.Add(new UranusMaterial(new UranusItem(segment[i].Trim()), count));
			}
		}

		public void LoadGenerator(string generatorString)
		{
			var segment = generatorString.Split(',');

			// Products
			for (int i = 0; i < 2; i += 2) // generator product column count : 1
			{
				if (string.IsNullOrEmpty(segment[i]))
				{
					break;
				}

				// count format
				// "2" => 2
				// "1.66^24" => 1.66*10^24
				var nSegment = segment[i + 1].Split('^');
				var count = nSegment.Length == 1 ?
					decimal.Parse(segment[i + 1]) :
					(decimal)(double.Parse(nSegment[0]) * Math.Pow(10, double.Parse(nSegment[1])));

				Products.Add(new UranusProduct(new UranusItem(segment[i].Trim()), count));
			}

			// Time
			Time = string.IsNullOrEmpty(segment[2]) ?
				0 :
				decimal.Parse(segment[2]);

			// Materials
			for (int i = 2 + 1; i < 2 + 1 + MaterialsColumnCount * 2; i += 2)
			{
				if (string.IsNullOrEmpty(segment[i]))
				{
					break;
				}

				var nSegment = segment[i + 1].Split('^');
				var count = nSegment.Length == 1 ?
					decimal.Parse(segment[i + 1]) :
					(decimal)(double.Parse(nSegment[0]) * Math.Pow(10, double.Parse(nSegment[1])));

				Materials.Add(new UranusMaterial(new UranusItem(segment[i].Trim()), count));
			}

			// Generates
			for (int i = 2 + 1 + MaterialsColumnCount * 2; i < 2 + 1 + MaterialsColumnCount * 2 + GeneratesColumnCount * 2; i += 2)
			{
				if (string.IsNullOrEmpty(segment[i]))
				{
					break;
				}

				var nSegment = segment[i + 1].Split('^');
				var count = nSegment.Length == 1 ?
					decimal.Parse(segment[i + 1]) :
					(decimal)(double.Parse(nSegment[0]) * Math.Pow(10, double.Parse(nSegment[1])));

				Generations.Add(new UranusGeneration(new UranusItem(segment[i].Trim()), count));
			}
		}

		public override string ToString()
		{
			return $"{string.Join(" + ", Materials)} = {string.Join(" + ", Products)}";
		}

		public bool Equals(UranusRecipe? other)
		{
			if (other == null)
			{
				return false;
			}

			return Products.SequenceEqual(other.Products) && Materials.SequenceEqual(other.Materials);
		}
	}
}
