using Newtonsoft.Json;
using System;
using System.Runtime.CompilerServices;

namespace Tools_protocol.Json
{
	public static class WorldEnums
	{
        public class Data
		{
			[JsonProperty("Tables_world")]
			public WorldEnums.Worldentries worldentries
			{
				get;
				set;
			}

			public Data()
			{
			}
		}

		public class Worldentries
		{
			[JsonProperty("drops")]
			public string Drops
			{
				get;
				set;
			}

			[JsonProperty("items")]
			public string Items
			{
				get;
				set;
			}

			[JsonProperty("monstres")]
			public string Monstres
			{
				get;
				set;
			}

			[JsonProperty("personnages")]
			public string personnages
			{
				get;
				set;
			}
			[JsonProperty("gifts")]
			public string Gifts
			{
				get;
				set;
			}

			public Worldentries()
			{
			}
		}
	}
}