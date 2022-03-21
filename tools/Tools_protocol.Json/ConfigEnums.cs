using Newtonsoft.Json;
using System;
using System.Runtime.CompilerServices;

namespace Tools_protocol.Json
{
	public class ConfigEnums
	{
		public ConfigEnums()
		{
		}

		public class ConfigEntries
		{
			[JsonProperty("Aname")]
			public string Cauth
			{
				get;
				set;
			}

			[JsonProperty("Wname")]
			public string Cworld
			{
				get;
				set;
			}

			[JsonProperty("Emu")]
			public string emu
			{
				get;
				set;
			}

			[JsonProperty("hote")]
			public string hote
			{
				get;
				set;
			}

			[JsonProperty("mdp")]
			public string mdp
			{
				get;
				set;
			}

			[JsonProperty("user")]
			public string user
			{
				get;
				set;
			}

			public ConfigEntries()
			{
			}
		}

		public class Data
		{
			[JsonProperty("Config")]
			public ConfigEntries ConfigEntries
			{
				get;
				set;
			}

			public Data()
			{
			}
		}
	}
}