﻿using System.Threading.Tasks;
using NextGenSoftware.OASIS.API.Core.Helpers;
using NextGenSoftware.OASIS.API.Core.Interfaces;
using NextGenSoftware.OASIS.STAR.Zomes;

namespace NextGenSoftware.OASIS.STAR.DNATemplates.CSharpTemplates
{
    public class ZomeDNATemplate : ZomeBase, IZome
    {
        //public ZomeDNATemplate(HoloNETClientBase holoNETClient) : base(holoNETClient, "{zome}")
        public ZomeDNATemplate() : base()
        {

        }

        /*
        public ZomeDNATemplate(string holochainConductorURI, HoloNETClientType type) : base(holochainConductorURI, "{zome}", type)
        {

        }*/

        public async Task<IHolon> LoadHOLONAsync(string hcEntryAddressHash)
        {
            //return await base.LoadHolonAsync("{holon}", hcEntryAddressHash);
            return await base.LoadHolonAsync(hcEntryAddressHash);
        }

        public async Task<OASISResult<IHolon>> SaveHOLONAsync(IHolon holon)
        {
            //return await base.SaveHolonAsync("{holon}", holon);
            return await base.SaveHolonAsync(holon);
        }
    }
}
