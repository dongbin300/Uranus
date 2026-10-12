using Newtonsoft.Json;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using Uranus.Interfaces;

namespace Uranus
{
	public class UranusItem : IBindable, IEquatable<UranusItem>
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		public void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		/// <summary>
		/// 아이템 이름 (고유 식별 값)
		/// 숫자만으로 이루어질 수 없고, 문자나 문자+숫자로 이루어져야 함
		/// (250204, KoreanName으로 대체됨, 더 이상 사용되지 않음)
		/// </summary>
		//private string name;
		//public string Name
		//{
		//	get => name;
		//	set
		//	{
		//		if (name != value)
		//		{
		//			name = value;
		//			OnPropertyChanged(nameof(Name));
		//		}
		//	}
		//}

		/// <summary>
		/// 아이템 한국어 이름(고유 식별 값)
		/// </summary>
		private string koreanName;
		public string KoreanName
		{
			get => koreanName;
			set
			{
				if (koreanName != value)
				{
					koreanName = value;
					OnPropertyChanged(nameof(KoreanName));
				}
			}
		}

		/// <summary>
		/// 아이템 설명
		/// </summary>
		private string comment;
		[JsonIgnore]
		public string Comment
		{
			get => comment;
			set
			{
				if (comment != value)
				{
					comment = value;
					OnPropertyChanged(nameof(Comment));
				}
			}
		}

		/// <summary>
		/// 아이템 한국어 설명
		/// </summary>
		private string koreanComment;
		[JsonIgnore]
		public string KoreanComment
		{
			get => koreanComment;
			set
			{
				if (koreanComment != value)
				{
					koreanComment = value;
					OnPropertyChanged(nameof(KoreanComment));
				}
			}
		}

		public UranusItem()
		{
			koreanName = string.Empty;
			comment = string.Empty;
			koreanComment = string.Empty;
		}

		[JsonConstructor]
		public UranusItem(string koreanName, string comment, string koreanComment)
		{
			this.koreanName = koreanName;
			this.comment = comment;
			this.koreanComment = koreanComment;
		}

		public UranusItem(string koreanName) : this(koreanName, "", "")
		{

		}

		public void LoadBaseItem(string baseItemString)
		{
			koreanName = baseItemString.Trim();
		}

		public override int GetHashCode()
		{
			return KoreanName.GetHashCode();
		}

		public override string ToString()
		{
			return KoreanName;
		}

		public bool Equals(UranusItem? other)
		{
			if (other == null)
			{
				return false;
			}
			return KoreanName == other.KoreanName;
		}
	}

	public class UranusItemComparer : IEqualityComparer<UranusItem>
	{
		public bool Equals(UranusItem? x, UranusItem? y)
		{
			if (x == null || y == null)
			{
				return false;
			}
			return x.KoreanName == y.KoreanName;
		}

		public int GetHashCode([DisallowNull] UranusItem obj)
		{
			return obj.KoreanName.GetHashCode();
		}
	}
}