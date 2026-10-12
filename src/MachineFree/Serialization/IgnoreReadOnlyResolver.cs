using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using System.Reflection;

namespace MachineFree.Serialization
{
	public class IgnoreReadOnlyResolver : DefaultContractResolver
	{
		protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
		{
			var property = base.CreateProperty(member, memberSerialization);

			if (property.Writable == false && property.Readable == true)
			{
				property.ShouldSerialize = _ => false;
			}

			return property;
		}
	}
}
