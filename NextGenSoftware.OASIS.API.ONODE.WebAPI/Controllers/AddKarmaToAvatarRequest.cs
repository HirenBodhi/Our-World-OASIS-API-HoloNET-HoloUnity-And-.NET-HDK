﻿using NextGenSoftware.OASIS.API.Core;
using System;

namespace NextGenSoftware.OASIS.API.ONODE.WebAPI.Controllers
{
    public class AddKarmaToAvatarRequest
    {
       // public Guid AvatarId { get; set; }
        //public KarmaTypePositive KarmaType { get; set; }
        //public KarmaSourceType karmaSourceType { get; set; }

        public string KarmaType { get; set; }
        public string karmaSourceType { get; set; }

        public string KaramSourceTitle { get; set; }

        public string KarmaSourceDesc { get; set; }
    }
}