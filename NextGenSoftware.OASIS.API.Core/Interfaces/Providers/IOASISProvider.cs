﻿using NextGenSoftware.OASIS.API.Core.Enums;
using NextGenSoftware.OASIS.API.Core.Helpers;
using System.Threading.Tasks;

namespace NextGenSoftware.OASIS.API.Core.Interfaces
{
    public interface IOASISProvider
    {
        string ProviderName { get; set; }
        string ProviderDescription { get; set; }
        EnumValue<ProviderCategory> ProviderCategory { get; set; } //= new EnumValue<ProviderCategory>(Enums.ProviderCategory.SmartContract);
        EnumValue<ProviderType> ProviderType { get; set; }
        //bool Activated { get; private set; } //TODO: Use this when upgrade to C# 8.0 (.NET Core 3.0).
        bool IsProviderActivated { get; }
        OASISResult<bool> ActivateProvider();
        Task<OASISResult<bool>> ActivateProviderAsync();
        OASISResult<bool> DeActivateProvider();
        Task<OASISResult<bool>> DeActivateProviderAsync();
    }
}