﻿using System.Runtime.Serialization;

namespace TraktPlugin.TraktAPI.DataStructures
{
    [DataContract]
    public class TraktMovieBoxOffice
    {
        [DataMember(Name = "revenue")]
        public int Revenue { get; set; }

        [DataMember(Name = "movie")]
        public TraktMovieSummary Movie { get; set; }
    }
}
