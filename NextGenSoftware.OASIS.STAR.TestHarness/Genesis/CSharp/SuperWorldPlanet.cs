using System;
using System.Threading.Tasks;
using NextGenSoftware.OASIS.API.Core.Enums;
using NextGenSoftware.OASIS.API.Core.Helpers;
using NextGenSoftware.OASIS.API.Core.Interfaces.STAR;
using NextGenSoftware.OASIS.STAR.CelestialBodies;

namespace NextGenSoftware.OASIS.STAR.TestHarness.Genesis
{
    public class SuperWorld : Planet, IPlanet
    {
        //public SuperWorld(string providerKey, ProviderType providerType, bool autoLoad = true) : base(providerKey, providerType, autoLoad)
        //{

        //}

        //public SuperWorld(Guid id, bool autoLoad = true) : base(id, autoLoad)
        //{

        //}

        public SuperWorld() : base(new Guid("46966c5f-64e6-41aa-beb5-202644340821"))
        {

        }

        public async Task<OASISResult<SuperTest>> LoadSuperTestAsync(Guid id)
        {
            return await base.CelestialBodyCore.LoadHolonAsync<SuperTest>(id);
        }

        public OASISResult<SuperTest> LoadSuperTest(Guid id)
        {
            return base.CelestialBodyCore.LoadHolon<SuperTest>(id);
        }

        //public async Task<OASISResult<T>> LoadSuperTestAsync<T>(Guid id) where T : IHolon
        //{
        //    return await base.CelestialBodyCore.LoadHolonAsync<T>(id);
        //}

        //public OASISResult<T> LoadSuperTest<T>(Guid id) where T : IHolon
        //{
        //    return base.CelestialBodyCore.LoadHolon<T>(id);
        //}

        //public async Task<OASISResult<IHolon>> LoadSuperTestAsync(Guid id)
        //{
        //    return await base.CelestialBodyCore.LoadHolonAsync(id);
        //}

        //public OASISResult<IHolon> LoadSuperTest(Guid id)
        //{
        //    return base.CelestialBodyCore.LoadHolon(id);
        //}

        public async Task<OASISResult<SuperTest>> LoadSuperTestAsync(ProviderType providerType, string providerKey)
        {
            return await base.CelestialBodyCore.LoadHolonAsync<SuperTest>(providerType, providerKey);
        }

        public OASISResult<SuperTest> LoadSuperTest(ProviderType providerType, string providerKey)
        {
            return base.CelestialBodyCore.LoadHolon<SuperTest>(providerType, providerKey);
        }

        //public async Task<OASISResult<T>> LoadSuperTestAsync<T>(ProviderType providerType, string providerKey) where T : IHolon
        //{
        //    return await base.CelestialBodyCore.LoadHolonAsync<T>(providerKey, providerType);
        //}

        //public OASISResult<T> LoadSuperTest(ProviderType providerType, string providerKey)
        //{
        //    return base.CelestialBodyCore.LoadHolon<T>(providerKey, providerType);
        //}

        //public async Task<OASISResult<IHolon>> LoadSuperTestAsync(ProviderType providerType, string providerKey)
        //{
        //    return await base.CelestialBodyCore.LoadHolonAsync(providerKey, providerType);
        //}

        //public OASISResult<IHolon> LoadSuperTest(ProviderType providerType, string providerKey)
        //{
        //    return base.CelestialBodyCore.LoadHolon(providerKey, providerType);
        //}

        public async Task<OASISResult<SuperTest>> SaveSuperTestAsync(SuperTest holon)
        {
            return await base.CelestialBodyCore.SaveHolonAsync<SuperTest>(holon);
        }

        public OASISResult<SuperTest> SaveSuperTest(SuperTest holon)
        {
            return base.CelestialBodyCore.SaveHolon<SuperTest>(holon);
        }

        //public async Task<OASISResult<T>> SaveSuperTestAsync<T>(T holon) where T : IHolon
        //{
        //    return await base.CelestialBodyCore.SaveHolonAsync<T>(holon);
        //}

        //public OASISResult<T> SaveSuperTest<T>(SuperTest holon) where T : IHolon
        //{
        //    return base.CelestialBodyCore.SaveHolon<SuperTest>(holon);
        //}

        //public async Task<OASISResult<IHolon>> SaveSuperTestAsync(IHolon holon)
        //{
        //    return await base.CelestialBodyCore.SaveHolonAsync(holon);
        //}

        //public OASISResult<IHolon> SaveSuperTest(IHolon holon)
        //{
        //    return base.CelestialBodyCore.SaveHolon(holon);
        //}
    }
}