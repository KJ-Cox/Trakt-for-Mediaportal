﻿using System.Runtime.Serialization;

namespace TraktPlugin.TraktAPI.DataStructures
{
    [DataContract]
    public class TraktPerson
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "ids")]
        public TraktPersonId Ids { get; set; }
    }
}
