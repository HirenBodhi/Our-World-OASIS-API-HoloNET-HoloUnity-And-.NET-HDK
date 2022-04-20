﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using BC = BCrypt.Net.BCrypt;
using NextGenSoftware.OASIS.API.Core.Enums;
using NextGenSoftware.OASIS.API.Core.Events;
using NextGenSoftware.OASIS.API.Core.Helpers;
using NextGenSoftware.OASIS.API.Core.Interfaces;
using NextGenSoftware.OASIS.API.Core.Objects;
using NextGenSoftware.OASIS.API.DNA;
using NextGenSoftware.OASIS.API.Core.Holons;

namespace NextGenSoftware.OASIS.API.Core.Managers
{
    public class AvatarManager : OASISManager
    {
        public static IAvatar LoggedInAvatar { get; set; }
        private ProviderManagerConfig _config;

        public List<IOASISStorageProvider> OASISStorageProviders { get; set; }

        public ProviderManagerConfig Config
        {
            get
            {
                if (_config == null)
                    _config = new ProviderManagerConfig();

                return _config;
            }
        }

        public enum SaveMode
        {
            FirstSaveAttempt,
            AutoFailOver,
            AutoReplication
        }

        public delegate void StorageProviderError(object sender, AvatarManagerErrorEventArgs e);

        //TODO: Not sure we want to pass the OASISDNA here?
        public AvatarManager(IOASISStorageProvider OASISStorageProvider, OASISDNA OASISDNA = null) : base(OASISStorageProvider, OASISDNA)
        {

        }

        // TODO: Not sure if we want to move methods from the AvatarService in WebAPI here?
        // For integration with STAR and others like Unity can just call the REST API service?
        // What advantage is there to making it native through dll's? Would be slightly faster than having to make a HTTP request/response round trip...
        // BUT would STILL need to call out to a OASIS Storage Provider so depending if that was also running locally is how fast it would be...
        // For now just easier to call the REST API service from STAR... can come back to this later... :)
        public OASISResult<IAvatar> Authenticate(string username, string password, string ipAddress)
        {
            //They can log in with either username, email or a public key linked to the avatar.
            OASISResult<IAvatar> result = null;

            try
            {
                //First try by username...
                result = LoadAvatar(username, false);

                if (result.Result == null)
                {
                    //Now try by email...
                    result = LoadAvatarByEmail(username, false);

                    if (result.Result == null)
                    {
                        //Finally by Public Key...
                        OASISResult<IEnumerable<IAvatar>> avatarsResult = LoadAllAvatars();

                        if (!avatarsResult.IsError && avatarsResult.Result != null)
                        {
                            result.Result = avatarsResult.Result.FirstOrDefault(x => x.ProviderPublicKey[ProviderManager.CurrentStorageProviderType.Value].Contains(username));

                            if (result.Result == null)
                                result.Message = $"This avatar does not exist. Please contact support or create a new avatar. Error Details: {result.Message}";
                        }
                    }
                }

                if (result.Result != null)
                {
                    if (result.Result.DeletedDate != DateTime.MinValue)
                    {
                        result.IsError = true;
                        result.Message = $"This avatar was deleted on {result.Result.DeletedDate} by avatar with id {result.Result.DeletedByAvatarId}, please contact support or create a new avatar with a new email address.";
                    }

                    // TODO: Implement Activate/Deactivate methods in AvatarManager & Providers...
                    if (!result.Result.IsActive)
                    {
                        result.IsError = true;
                        result.Message = "This avatar is no longer active. Please contact support or create a new avatar.";
                    }

                    //if (!result.Result.IsVerified && OASISDNA.OASIS.Security.DoesAvatarNeedToBeVerifiedBeforeLogin)
                    if (!result.Result.IsVerified && OASISDNA.OASIS.Email.SendVerificationEmail)
                    {
                        result.IsError = true;
                        result.Message = "Avatar has not been verified. Please check your email.";
                    }

                    if (!BC.Verify(password, result.Result.Password))
                    {
                        result.IsError = true;
                        result.Message = "Email or password is incorrect";
                    }
                }

                //TODO: Come back to this.
                //if (OASISDNA.OASIS.Security.AvatarPassword.)

                if (result.Result != null & !result.IsError)
                {
                    var jwtToken = GenerateJWTToken(result.Result);
                    var refreshToken = generateRefreshToken(ipAddress);

                    result.Result.RefreshTokens.Add(refreshToken);
                    result.Result.JwtToken = jwtToken;
                    result.Result.RefreshToken = refreshToken.Token;
                    result.Result.LastBeamedIn = DateTime.Now;
                    result.Result.IsBeamedIn = true;

                    LoggedInAvatar = result.Result;
                    OASISResult<IAvatar> saveAvatarResult = SaveAvatar(result.Result);

                    if (!saveAvatarResult.IsError && saveAvatarResult.IsSaved)
                    {
                        result.Result = HideAuthDetails(saveAvatarResult.Result);
                        result.IsSaved = true;
                        result.Message = "Avatar Successfully Authenticated.";
                    }
                    else
                        ErrorHandling.HandleError(ref result, $"Error occured in Authenticate method in AvatarManager whilst saving the avatar. Reason: {saveAvatarResult.Message}");
                }
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured in Authenticate method in AvatarManager. Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatar>> AuthenticateAsync(string username, string password, string ipAddress)
        {
            //They can log in with either username, email or a public key linked to the avatar.
            OASISResult<IAvatar> result = null;

            try
            {
                //First try by username...
                result = await LoadAvatarAsync(username, false);

                if (result.Result == null)
                {
                    //Now try by email...
                    result = await LoadAvatarByEmailAsync(username, false);

                    if (result.Result == null)
                    {
                        //Finally by Public Key...
                        OASISResult<IEnumerable<IAvatar>> avatarsResult = await LoadAllAvatarsAsync();
                        
                        if (!avatarsResult.IsError && avatarsResult.Result != null)
                        {
                            result.Result = avatarsResult.Result.FirstOrDefault(x => x.ProviderPublicKey[ProviderManager.CurrentStorageProviderType.Value].Contains(username));

                            if (result.Result == null)
                                result.Message = $"This avatar does not exist. Please contact support or create a new avatar. Error Details: {result.Message}";
                        }
                    }
                }
                
                if (result.Result != null)
                {
                    if (result.Result.DeletedDate != DateTime.MinValue)
                    {
                        result.IsError = true;
                        result.Message = $"This avatar was deleted on {result.Result.DeletedDate} by avatar with id {result.Result.DeletedByAvatarId}, please contact support or create a new avatar with a new email address.";
                    }

                    if (!result.Result.IsActive)
                    {
                        result.IsError = true;
                        result.Message = "This avatar is no longer active. Please contact support or create a new avatar.";
                    }

                    if (!result.Result.IsVerified)
                    {
                        result.IsError = true;
                        result.Message = "Avatar has not been verified. Please check your email.";
                    }

                    if (!BC.Verify(password, result.Result.Password))
                    {
                        result.IsError = true;
                        result.Message = "Email or password is incorrect";
                    }
                }

                //TODO: Come back to this.
                //if (OASISDNA.OASIS.Security.AvatarPassword.)

                if (result.Result != null & !result.IsError)
                {
                    var jwtToken = GenerateJWTToken(result.Result);
                    var refreshToken = generateRefreshToken(ipAddress);

                    result.Result.RefreshTokens.Add(refreshToken);
                    result.Result.JwtToken = jwtToken;
                    result.Result.RefreshToken = refreshToken.Token;
                    result.Result.LastBeamedIn = DateTime.Now;
                    result.Result.IsBeamedIn = true;

                    LoggedInAvatar = result.Result;
                    OASISResult<IAvatar> saveAvatarResult = SaveAvatar(result.Result);

                    if (!saveAvatarResult.IsError && saveAvatarResult.IsSaved)
                    {
                        result.Result = HideAuthDetails(saveAvatarResult.Result);
                        result.IsSaved = true;
                        result.Message = "Avatar Successfully Authenticated.";
                    }
                    else
                        ErrorHandling.HandleError(ref result, $"Error occured in AuthenticateAsync method in AvatarManager whilst saving the avatar. Reason: {saveAvatarResult.Message}");
                }
                else
                    result.Result = null;
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured in AuthenticateAsync method in AvatarManager. Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public OASISResult<IAvatar> Register(string avatarTitle, string firstName, string lastName, string email, string password, AvatarType avatarType, string origin, OASISType createdOASISType, ConsoleColor cliColour = ConsoleColor.Green, ConsoleColor favColour = ConsoleColor.Green)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();

            try
            {
                result = PrepareToRegisterAvatar(avatarTitle, firstName, lastName, email, password, avatarType, origin, createdOASISType);

                if (result != null && !result.IsError && result.Result != null)
                {
                    // AvatarDetail needs to have the same unique ID as Avatar so the records match (they will have unique/different provider keys per each provider)
                    OASISResult<IAvatarDetail> avatarDetailResult = PrepareToRegisterAvatarDetail(result.Result.Id, result.Result.Username, result.Result.Email, createdOASISType, cliColour, favColour);

                    if (avatarDetailResult != null && !avatarDetailResult.IsError && avatarDetailResult.Result != null)
                    {
                        OASISResult<IAvatar> saveAvatarResult = SaveAvatar(result.Result);

                        if (!saveAvatarResult.IsError && saveAvatarResult.IsSaved)
                        {
                            result.Result = saveAvatarResult.Result;
                            OASISResult<IAvatarDetail> saveAvatarDetailResult = SaveAvatarDetail(avatarDetailResult.Result);

                            if (saveAvatarDetailResult != null && !saveAvatarDetailResult.IsError && saveAvatarDetailResult.Result != null)
                                result = AvatarRegistered(result, origin);
                            else
                            {
                                result.Message = saveAvatarDetailResult.Message;
                                result.IsError = saveAvatarDetailResult.IsError;
                                result.IsSaved = saveAvatarDetailResult.IsSaved;
                            }
                        }
                        else
                        {
                            result.Message = saveAvatarResult.Message;
                            result.IsError = saveAvatarResult.IsError;
                            result.IsSaved = saveAvatarResult.IsSaved;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured in Register method in AvatarManager. Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatar>> RegisterAsync(string avatarTitle, string firstName, string lastName, string email, string password, AvatarType avatarType, string origin, OASISType createdOASISType, ConsoleColor cliColour = ConsoleColor.Green, ConsoleColor favColour = ConsoleColor.Green)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();

            try
            {
                result = PrepareToRegisterAvatar(avatarTitle, firstName, lastName, email, password, avatarType, origin, createdOASISType);


                if (result != null && !result.IsError && result.Result != null)
                {
                    // AvatarDetail needs to have the same unique ID as Avatar so the records match (they will have unique/different provider keys per each provider)
                    OASISResult<IAvatarDetail> avatarDetailResult = PrepareToRegisterAvatarDetail(result.Result.Id, result.Result.Username, result.Result.Email, createdOASISType, cliColour, favColour);

                    if (avatarDetailResult != null && !avatarDetailResult.IsError && avatarDetailResult.Result != null)
                    {
                        OASISResult<IAvatar> saveAvatarResult = await SaveAvatarAsync(result.Result);

                        if (!saveAvatarResult.IsError && saveAvatarResult.IsSaved)
                        {
                            result.Result = saveAvatarResult.Result;
                            OASISResult<IAvatarDetail> saveAvatarDetailResult = await SaveAvatarDetailAsync(avatarDetailResult.Result);

                            if (saveAvatarDetailResult != null && !saveAvatarDetailResult.IsError && saveAvatarDetailResult.Result != null)
                                result = AvatarRegistered(result, origin);
                            else
                            {
                                result.Message = saveAvatarDetailResult.Message;
                                result.IsError = saveAvatarDetailResult.IsError;
                                result.IsSaved = saveAvatarDetailResult.IsSaved;
                            }
                        }
                        else
                        {
                            result.Message = saveAvatarResult.Message;
                            result.IsError = saveAvatarResult.IsError;
                            result.IsSaved = saveAvatarResult.IsSaved;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured in RegisterAsync method in AvatarManager. Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public OASISResult<bool> VerifyEmail(string token)
        {
            OASISResult<bool> result = new OASISResult<bool>();

            try
            {
                //TODO: PERFORMANCE} Implement in Providers so more efficient and do not need to return whole list!
                OASISResult<IEnumerable<IAvatar>> avatarsResult = LoadAllAvatars(false);

                if (!avatarsResult.IsError && avatarsResult.Result != null)
                {
                    IAvatar avatar = avatarsResult.Result.FirstOrDefault(x => x.VerificationToken == token);

                    if (avatar == null)
                    {
                        result.Result = false;
                        result.IsError = true;
                        result.Message = "Verification Failed";
                    }
                    else
                    {
                        result.Result = true;
                        avatar.Verified = DateTime.UtcNow;
                        avatar.VerificationToken = null;
                        avatar.IsActive = true;
                        OASISResult<IAvatar> saveAvatarResult = SaveAvatar(avatar);

                        result.IsError = saveAvatarResult.IsError;
                        result.IsSaved = saveAvatarResult.IsSaved;
                        result.Message = saveAvatarResult.Message;
                    }
                }
                else
                    ErrorHandling.HandleError(ref result, $"Error in VerifyEmail loading all avatars. Reason: {avatarsResult.Message}");

                if (!result.IsError && result.IsSaved)
                {
                    result.Message = "Verification successful, you can now login";
                    result.Result = true;
                }
                else
                    result.Result = false;
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured in VerifyEmail method in AvatarManager. Error Message: ", ex.Message), ex);
                result.Result = false;
            }

            return result;
        }

        //TODO: Finish moving Update methods and ALL AvatarService methods here ASAP!
        //Update also needs to be able to update ANY avatar property, currently it is only email, name, etc.

        /*
        public async Task<OASISResult<IAvatar>> Update(Guid id, UpdateRequest avatar)
        {
            var response = new OASISResult<IAvatar>();
            string errorMessage = "Error in Update method in Avatar Service. Reason: ";

            try
            {
                response = await AvatarManager.LoadAvatarAsync(id, false);

                if (response.IsError || response.Result == null)
                    ErrorHandling.HandleError(ref response, $"{errorMessage}{response.Message}", response.DetailedMessage);
                else
                    response = await Update(response.Result, avatar);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref response, $"{errorMessage}Unknown Error Occured. See DetailedMessage for more info.", ex.Message, ex);
            }

            return response;
        }

        public async Task<OASISResult<IAvatar>> UpdateByEmail(string email, UpdateRequest avatar)
        {
            var response = new OASISResult<IAvatar>();
            string errorMessage = "Error in UpdateByEmail method in Avatar Service. Reason: ";

            try
            {
                response = await AvatarManager.LoadAvatarByEmailAsync(email);

                if (response.IsError || response.Result == null)
                    ErrorHandling.HandleError(ref response, $"{errorMessage}{response.Message}", response.DetailedMessage);
                else
                    response = await Update(response.Result, avatar);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref response, $"{errorMessage}Unknown Error Occured. See DetailedMessage for more info.", ex.Message, ex);
            }

            return response;
        }

        public async Task<OASISResult<IAvatar>> UpdateByUsername(string username, UpdateRequest avatar)
        {
            var response = new OASISResult<IAvatar>();
            string errorMessage = "Error in UpdateByUsername method in Avatar Service. Reason: ";

            try
            {
                response = await AvatarManager.LoadAvatarAsync(username);

                if (response.IsError || response.Result == null)
                    ErrorHandling.HandleError(ref response, $"{errorMessage}{response.Message}", response.DetailedMessage);
                else
                    response = await Update(response.Result, avatar);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref response, $"{errorMessage}Unknown Error Occured. See DetailedMessage for more info.", ex.Message, ex);
            }

            return response;
        }*/

        public OASISResult<IAvatar> LoadAvatar(Guid id, bool hideAuthDetails = true, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = LoadAvatarForProvider(id, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = LoadAvatarForProvider(id, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar, ", id, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString(), "."), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar ", id, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString(), "."), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Loaded.";

                    if (hideAuthDetails)
                        result.Result = HideAuthDetails(result.Result);
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar with id ", id, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatar>> LoadAvatarAsync(Guid id, bool hideAuthDetails = true, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await LoadAvatarForProviderAsync(id, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await LoadAvatarForProviderAsync(id, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar, ", id, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar ", id, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Loaded.";

                    if (hideAuthDetails)
                        result.Result = HideAuthDetails(result.Result);
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar with id ", id, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public OASISResult<IAvatar> LoadAvatar(string username, string password, bool hideAuthDetails = true, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = LoadAvatarForProvider(username, password, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = LoadAvatarForProvider(username, password, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar, ", username, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar ", username, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Loaded.";

                    if (hideAuthDetails)
                        result.Result = HideAuthDetails(result.Result);
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar with username ", username, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatar>> LoadAvatarAsync(string username, string password, bool hideAuthDetails = true, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await LoadAvatarForProviderAsync(username, password, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != providerType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await LoadAvatarForProviderAsync(username, password, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar, ", username, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar ", username, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Loaded.";

                    if (hideAuthDetails)
                        result.Result = HideAuthDetails(result.Result);
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar with username ", username, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        //TODO: Replicate Auto-Fail over and Auto-Replication code for all Avatar, HolonManager methods etc...
        public OASISResult<IAvatar> LoadAvatar(string username, bool hideAuthDetails = true, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = LoadAvatarForProvider(username, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = LoadAvatarForProvider(username, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar, ", username, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar ", username, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Loaded.";

                    if (hideAuthDetails)
                        result.Result = HideAuthDetails(result.Result);
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar with username ", username, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatar>> LoadAvatarAsync(string username, bool hideAuthDetails = true, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await LoadAvatarForProviderAsync(username, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await LoadAvatarForProviderAsync(username, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar, ", username, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    //ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar, ", username, ". Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar ", username, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Loaded.";

                    if (hideAuthDetails)
                        result.Result = HideAuthDetails(result.Result);
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar with username ", username, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public OASISResult<IAvatar> LoadAvatarByEmail(string email, bool hideAuthDetails = true, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = LoadAvatarByEmailForProvider(email, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = LoadAvatarByEmailForProvider(email, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar with email ", email, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with email ", email, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Loaded.";

                    if (hideAuthDetails)
                        result.Result = HideAuthDetails(result.Result);
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar with email ", email, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatar>> LoadAvatarByEmailAsync(string email, bool hideAuthDetails = true, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await LoadAvatarByEmailForProviderAsync(email, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await LoadAvatarByEmailForProviderAsync(email, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar with email ", email, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with email ", email, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Loaded.";

                    if (hideAuthDetails)
                        result.Result = HideAuthDetails(result.Result);
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar with email ", email, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public OASISResult<IAvatarDetail> LoadAvatarDetail(Guid id, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatarDetail> result = new OASISResult<IAvatarDetail>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = LoadAvatarDetailForProvider(id, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = LoadAvatarDetailForProvider(id, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar with id ", id, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar detail with id ", id, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Detail Successfully Loaded.";   
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar detail with id ", id, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatarDetail>> LoadAvatarDetailAsync(Guid id, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatarDetail> result = new OASISResult<IAvatarDetail>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await LoadAvatarDetailForProviderAsync(id, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await LoadAvatarDetailForProviderAsync(id, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar detail with id ", id, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar detail with id ", id, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Detail Successfully Loaded.";
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar detail with id ", id, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatarDetail>> LoadAvatarDetailByEmailAsync(string email, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatarDetail> result = new OASISResult<IAvatarDetail>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await LoadAvatarDetailByEmailForProviderAsync(email, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await LoadAvatarDetailByEmailForProviderAsync(email, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar detail with email ", email, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar detail with email ", email, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Detail Successfully Loaded.";
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar detail with email ", email, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public OASISResult<IAvatarDetail> LoadAvatarDetailByEmail(string email, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatarDetail> result = new OASISResult<IAvatarDetail>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = LoadAvatarDetailByEmailForProvider(email, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = LoadAvatarDetailByEmailForProvider(email, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar detail with email ", email, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar detail with email ", email, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Detail Successfully Loaded.";   
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar detail with email ", email, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatarDetail>> LoadAvatarDetailByUsernameAsync(string username, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatarDetail> result = new OASISResult<IAvatarDetail>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await LoadAvatarDetailByUsernameForProviderAsync(username, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await LoadAvatarDetailByEmailForProviderAsync(username, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar detail with username ", username, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar detail with username ", username, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Detail Successfully Loaded.";
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar detail with username ", username, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public OASISResult<IAvatarDetail> LoadAvatarDetailByUsername(string username, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatarDetail> result = new OASISResult<IAvatarDetail>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = LoadAvatarDetailByUsernameForProvider(username, result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = LoadAvatarDetailByEmailForProvider(username, result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar detail with username ", username, ". Mostly likely reason is the avatar does not exist. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar detail with username ", username, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Details: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Detail Successfully Loaded.";
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading avatar detail with username ", username, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public OASISResult<IEnumerable<IAvatar>> LoadAllAvatars(bool hideAuthDetails = true, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IEnumerable<IAvatar>> result = new OASISResult<IEnumerable<IAvatar>>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = LoadAllAvatarsForProvider(result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = LoadAllAvatarsForProvider(result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load all avatars. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("All avatars loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatars Successfully Loaded.";

                    if (hideAuthDetails)
                        result.Result = HideAuthDetails(result.Result);
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading all avatars for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IEnumerable<IAvatar>>> LoadAllAvatarsAsync(bool hideAuthDetails = true, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IEnumerable<IAvatar>> result = new OASISResult<IEnumerable<IAvatar>>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await LoadAllAvatarsForProviderAsync(result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await LoadAllAvatarsForProviderAsync(result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load all avatars. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("All avatars loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatars Successfully Loaded.";

                    if (hideAuthDetails)
                        result.Result = HideAuthDetails(result.Result);
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading all avatars for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public OASISResult<IEnumerable<IAvatarDetail>> LoadAllAvatarDetails(ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IEnumerable<IAvatarDetail>> result = new OASISResult<IEnumerable<IAvatarDetail>>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = LoadAllAvatarDetailsForProvider(result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = LoadAllAvatarDetailsForProvider(result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load all avatar details. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("All avatar details loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Details Successfully Loaded.";
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading all avatar details for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IEnumerable<IAvatarDetail>>> LoadAllAvatarDetailsAsync(ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IEnumerable<IAvatarDetail>> result = new OASISResult<IEnumerable<IAvatarDetail>>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await LoadAllAvatarDetailsForProviderAsync(result, providerType, version);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await LoadAllAvatarDetailsForProviderAsync(result, type.Value, version);

                            if (!result.IsError && result.Result != null)
                                break;
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load all avatar details. Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsLoaded = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("All avatar details loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Details Successfully Saved";
                }

                // Set the current provider back to the original provider.
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured loading all avatar details for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatar>> SaveAvatarAsync(IAvatar avatar, ProviderType providerType = ProviderType.Default)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                int removingDays = OASISDNA.OASIS.Security.RemoveOldRefreshTokensAfterXDays;
                int removeQty = avatar.RefreshTokens.RemoveAll(token => (DateTime.Today - token.Created).TotalDays > removingDays);

                result = await SaveAvatarForProviderAsync(avatar, result, SaveMode.FirstSaveAttempt, providerType);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await SaveAvatarForProviderAsync(avatar, result, SaveMode.AutoFailOver, type.Value);

                            if (!result.IsError && result.Result != null)
                            {
                                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;
                                break;
                            }
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to save the avatar ", avatar.Name, " with id ", avatar.Id, ". Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsSaved = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar ", avatar.Name, " with id ", avatar.Id, " successfully saved for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to save for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Saved.";

                    //TODO: Need to move into background thread ASAP!
                    //TODO: Even if all providers failed above, we should still attempt again in a background thread for a fixed number of attempts (default 3) every X seconds (default 5) configured in OASISDNA.json.
                    //TODO: Auto-Failover should also re-try in a background thread after reporting the intial error above and then report after the retries either failed or succeeded later...
                    if (ProviderManager.IsAutoReplicationEnabled)
                    {
                        foreach (EnumValue<ProviderType> type in ProviderManager.GetProvidersThatAreAutoReplicating())
                        {
                            if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                                result = await SaveAvatarForProviderAsync(avatar, result, SaveMode.AutoReplication, type.Value);
                        }

                        if (result.WarningCount > 0)
                            ErrorHandling.HandleWarning(ref result, string.Concat("The avatar ", avatar.Name, " with id ", avatar.Id, " successfully saved for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to auto-replicate for some of the other providers in the Auto-Replicate List. Providers in the list are: ", ProviderManager.GetProvidersThatAreAutoReplicatingAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)), true);
                        else
                            LoggingManager.Log("Avatar Successfully Saved/Replicated", LogType.Info, ref result, true, false);
                    }
                }

                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured saving avatar ", avatar.Name, " with id ", avatar.Id, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public OASISResult<IAvatar> SaveAvatar(IAvatar avatar, ProviderType providerType = ProviderType.Default)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                int removingDays = OASISDNA.OASIS.Security.RemoveOldRefreshTokensAfterXDays;
                int removeQty = avatar.RefreshTokens.RemoveAll(token => (DateTime.Today - token.Created).TotalDays > removingDays);

                result = SaveAvatarForProvider(avatar, result, SaveMode.FirstSaveAttempt, providerType);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = SaveAvatarForProvider(avatar, result, SaveMode.AutoFailOver, type.Value);

                            if (!result.IsError && result.Result != null)
                            {
                                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;
                                break;
                            }
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to save the avatar ", avatar.Name, " with id ", avatar.Id, ". Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsSaved = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar ", avatar.Name, " with id ", avatar.Id, " successfully saved for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to save for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Saved.";

                    //TODO: Need to move into background thread ASAP!
                    //TODO: Even if all providers failed above, we should still attempt again in a background thread for a fixed number of attempts (default 3) every X seconds (default 5) configured in OASISDNA.json.
                    //TODO: Auto-Failover should also re-try in a background thread after reporting the intial error above and then report after the retries either failed or succeeded later...
                    if (ProviderManager.IsAutoReplicationEnabled)
                    {
                        foreach (EnumValue<ProviderType> type in ProviderManager.GetProvidersThatAreAutoReplicating())
                        {
                            if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                                result = SaveAvatarForProvider(avatar, result, SaveMode.AutoReplication, type.Value);
                        }

                        if (result.WarningCount > 0)
                            ErrorHandling.HandleWarning(ref result, string.Concat("The avatar ", avatar.Name, " with id ", avatar.Id, " successfully saved for the provider ", previousProviderType, " but failed to auto-replicate for some of the other providers in the Auto-Replicate List. Providers in the list are: ", ProviderManager.GetProvidersThatAreAutoReplicatingAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)), true);
                        else
                            LoggingManager.Log("Avatar Successfully Saved/Replicated", LogType.Info, ref result, true, false);
                    }
                }

                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured saving avatar ", avatar.Name, " with id ", avatar.Id, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public OASISResult<IAvatarDetail> SaveAvatarDetail(IAvatarDetail avatar, ProviderType providerType = ProviderType.Default)
        {
            OASISResult<IAvatarDetail> result = new OASISResult<IAvatarDetail>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = SaveAvatarDetailForProvider(avatar, result, SaveMode.FirstSaveAttempt, providerType);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = SaveAvatarDetailForProvider(avatar, result, SaveMode.AutoFailOver, type.Value);

                            if (!result.IsError && result.Result != null)
                            {
                                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;
                                break;
                            }
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to save the avatar detail ", avatar.Name, " with id ", avatar.Id, ". Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsSaved = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar detail ", avatar.Name, " with id ", avatar.Id, " successfully saved for the provider ", previousProviderType, " but failed to save for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Saved.";

                    //TODO: Need to move into background thread ASAP!
                    if (ProviderManager.IsAutoReplicationEnabled)
                    {
                        foreach (EnumValue<ProviderType> type in ProviderManager.GetProvidersThatAreAutoReplicating())
                        {
                            if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                                result = SaveAvatarDetailForProvider(avatar, result, SaveMode.AutoReplication, type.Value);
                        }

                        if (result.WarningCount > 0)
                            ErrorHandling.HandleWarning(ref result, string.Concat("The avatar detail ", avatar.Name, " with id ", avatar.Id, " successfully saved for the provider ", previousProviderType, " but failed to auto-replicate for some of the other providers in the Auto-Replicate List. Providers in the list are: ", ProviderManager.GetProvidersThatAreAutoReplicatingAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)), true);
                        else
                            LoggingManager.Log("Avatar Detail Successfully Saved/Replicated", LogType.Info, ref result, true, false);
                    }
                }

                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured saving avatar detail ", avatar.Name, " with id ", avatar.Id, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatarDetail>> SaveAvatarDetailAsync(IAvatarDetail avatar, ProviderType providerType = ProviderType.Default)
        {
            OASISResult<IAvatarDetail> result = new OASISResult<IAvatarDetail>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await SaveAvatarDetailForProviderAsync(avatar, result, SaveMode.FirstSaveAttempt, providerType);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await SaveAvatarDetailForProviderAsync(avatar, result, SaveMode.AutoFailOver, type.Value);

                            if (!result.IsError && result.Result != null)
                            {
                                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;
                                break;
                            }
                        }
                    }
                }

                if (result.Result == null)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to save the avatar detail ", avatar.Name, " with id ", avatar.Id, ". Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsSaved = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar detail ", avatar.Name, " with id ", avatar.Id, " successfully saved for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to save for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Detail Successfully Saved.";

                    //TODO: Need to move into background thread ASAP!
                    if (ProviderManager.IsAutoReplicationEnabled)
                    {
                        foreach (EnumValue<ProviderType> type in ProviderManager.GetProvidersThatAreAutoReplicating())
                        {
                            if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                                result = await SaveAvatarDetailForProviderAsync(avatar, result, SaveMode.AutoReplication, type.Value);
                        }

                        if (result.WarningCount > 0)
                            ErrorHandling.HandleWarning(ref result, string.Concat("The avatar detail ", avatar.Name, " with id ", avatar.Id, " successfully saved for the provider ", previousProviderType, " but failed to auto-replicate for some of the other providers in the Auto-Replicate List. Providers in the list are: ", ProviderManager.GetProvidersThatAreAutoReplicatingAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)), true);
                        else
                            LoggingManager.Log("Avatar Detail Successfully Saved/Replicated", LogType.Info, ref result, true, false);
                    }
                }

                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured saving avatar detail ", avatar.Name, " with id ", avatar.Id, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = null;
            }

            return result;
        }

        public async Task<OASISResult<IAvatarDetail>> UpdateAvatarDetailAsync(Guid id, IAvatarDetail avatarDetail)
        {
            OASISResult<IAvatarDetail> result = new OASISResult<IAvatarDetail>();
            string errorMessage = "Error in UpdateAvatarDetailAsync method in AvatarManager. Reason: ";

            OASISResult<IAvatarDetail> avatarDetailOriginalResult = await LoadAvatarDetailAsync(id);

            if (!avatarDetailOriginalResult.IsError && avatarDetailOriginalResult.Result != null)
            {
                if (avatarDetailOriginalResult.Result.Address != avatarDetail.Address)
                    avatarDetailOriginalResult.Result.Address = avatarDetail.Address;

                //TODO: Apply to all other properties. Use AutoMapper here instead! ;-)

                result = await SaveAvatarDetailAsync(avatarDetailOriginalResult.Result);

                if (!result.IsError && result.Result != null)
                {
                    OASISResult<IAvatar> avatarResult = await LoadAvatarAsync(avatarDetail.Id);

                    if (!avatarResult.IsError && avatarResult.Result != null)
                    {
                        if (avatarResult.Result.Username != avatarDetail.Username || avatarResult.Result.Email != avatarDetail.Email)
                        {
                            avatarResult.Result.Username = avatarDetail.Username;
                            avatarResult.Result.Email = avatarDetail.Email;

                            OASISResult<IAvatar> saveAvatarResult = await avatarResult.Result.SaveAsync();

                            if (!saveAvatarResult.IsError && saveAvatarResult.Result != null)
                            {
                                result.Message = "Avatar Detail Updated Successfully";
                                result.IsSaved = true;
                            }
                            else
                                ErrorHandling.HandleError(ref result, $"{errorMessage}{saveAvatarResult.Message}", saveAvatarResult.DetailedMessage);
                        }
                        else
                        {
                            result.Message = "Avatar Detail Updated Successfully";
                            result.IsSaved = true;
                        }
                    }
                    else
                        ErrorHandling.HandleError(ref result, $"{errorMessage}{avatarResult.Message}", avatarResult.DetailedMessage);
                }
                else
                    ErrorHandling.HandleError(ref result, $"{errorMessage}{result.Message}", result.DetailedMessage);
            }
            else
                ErrorHandling.HandleError(ref result, $"{errorMessage}{avatarDetailOriginalResult.Message}", avatarDetailOriginalResult.DetailedMessage);

            return result;
        }

        public OASISResult<bool> DeleteAvatar(Guid id, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            OASISResult<bool> result = new OASISResult<bool>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = DeleteAvatarForProvider(id, result, SaveMode.FirstSaveAttempt, softDelete, providerType);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if ((!result.Result || result.IsError) && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = DeleteAvatarForProvider(id, result, SaveMode.AutoFailOver, softDelete, type.Value);

                            if (!result.IsError && result.Result)
                            {
                                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;
                                break;
                            }
                        }
                    }
                }

                if (result.IsError || !result.Result)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to delete the avatar with id ", id, ". Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsSaved = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with id ", id, " was successfully deleted for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to delete for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Deleted.";

                    //TODO: Need to move into background thread ASAP!
                    if (ProviderManager.IsAutoReplicationEnabled)
                    {
                        foreach (EnumValue<ProviderType> type in ProviderManager.GetProvidersThatAreAutoReplicating())
                        {
                            if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                                result = DeleteAvatarForProvider(id, result, SaveMode.AutoReplication, softDelete, type.Value);
                        }

                        if (result.WarningCount > 0)
                            ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with id ", id, " was successfully deleted for the provider ", previousProviderType, " but failed to auto-replicate for some of the other providers in the Auto-Replicate List. Providers in the list are: ", ProviderManager.GetProvidersThatAreAutoReplicatingAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)), true);
                        else
                            LoggingManager.Log("Avatar Successfully Deleted/Replicated", LogType.Info, ref result, true, false);
                    }
                }

                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured deleting the avatar with id ", id, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = false;
            }

            return result;
        }

        public async Task<OASISResult<bool>> DeleteAvatarAsync(Guid id, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            OASISResult<bool> result = new OASISResult<bool>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await DeleteAvatarForProviderAsync(id, result, SaveMode.FirstSaveAttempt, softDelete, providerType);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if ((!result.Result || result.IsError) && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await DeleteAvatarForProviderAsync(id, result, SaveMode.AutoFailOver, softDelete, type.Value);

                            if (!result.IsError && result.Result)
                            {
                                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;
                                break;
                            }
                        }
                    }
                }

                if (result.IsError || !result.Result)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to delete the avatar with id ", id, ". Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsSaved = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with id ", id, " was successfully deleted for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to delete for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Deleted.";

                    //TODO: Need to move into background thread ASAP!
                    if (ProviderManager.IsAutoReplicationEnabled)
                    {
                        foreach (EnumValue<ProviderType> type in ProviderManager.GetProvidersThatAreAutoReplicating())
                        {
                            if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                                result = await DeleteAvatarForProviderAsync(id, result, SaveMode.AutoReplication, softDelete, type.Value);
                        }

                        if (result.WarningCount > 0)
                            ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with id ", id, " was successfully deleted for the provider ", previousProviderType, " but failed to auto-replicate for some of the other providers in the Auto-Replicate List. Providers in the list are: ", ProviderManager.GetProvidersThatAreAutoReplicatingAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)), true);
                        else
                            LoggingManager.Log("Avatar Successfully Deleted/Replicated", LogType.Info, ref result, true, false);
                    }
                }

                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured deleting the avatar with id ", id, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = false;
            }

            return result;
        }

        public async Task<OASISResult<bool>> DeleteAvatarByUsernameAsync(string userName, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            OASISResult<bool> result = new OASISResult<bool>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await DeleteAvatarByUsernameForProviderAsync(userName, result, SaveMode.FirstSaveAttempt, softDelete, providerType);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if ((!result.Result || result.IsError) && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await DeleteAvatarByUsernameForProviderAsync(userName, result, SaveMode.AutoReplication, softDelete, type.Value);

                            if (!result.IsError && result.Result)
                            {
                                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;
                                break;
                            }
                        }
                    }
                }

                if (result.IsError || !result.Result)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to delete the avatar with username ", userName, ". Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsSaved = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with username ", userName, " was successfully deleted for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to delete for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Deleted.";

                    //TODO: Need to move into background thread ASAP!
                    if (ProviderManager.IsAutoReplicationEnabled)
                    {
                        foreach (EnumValue<ProviderType> type in ProviderManager.GetProvidersThatAreAutoReplicating())
                        {
                            if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                                result = await DeleteAvatarByUsernameForProviderAsync(userName, result, SaveMode.AutoReplication, softDelete, type.Value);
                        }

                        if (result.WarningCount > 0)
                            ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with username ", userName, " was successfully deleted for the provider ", previousProviderType, " but failed to auto-replicate for some of the other providers in the Auto-Replicate List. Providers in the list are: ", ProviderManager.GetProvidersThatAreAutoReplicatingAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)), true);
                        else
                            LoggingManager.Log("Avatar Successfully Deleted/Replicated", LogType.Info, ref result, true, false);
                    }
                }

                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured deleting the avatar with userName ", userName, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = false;
            }

            return result;
        }

        public async Task<OASISResult<bool>> DeleteAvatarByEmailAsync(string email, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            OASISResult<bool> result = new OASISResult<bool>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = await DeleteAvatarByEmailForProviderAsync(email, result, SaveMode.FirstSaveAttempt, softDelete, providerType);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if ((!result.Result || result.IsError) && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = await DeleteAvatarByEmailForProviderAsync(email, result, SaveMode.AutoFailOver, softDelete, type.Value);

                            if (!result.IsError && result.Result)
                            {
                                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;
                                break;
                            }
                        }
                    }
                }

                if (result.IsError || !result.Result)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to delete the avatar with email ", email, ". Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsSaved = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with email ", email, " was successfully deleted for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to delete for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Deleted.";

                    //TODO: Need to move into background thread ASAP!
                    if (ProviderManager.IsAutoReplicationEnabled)
                    {
                        foreach (EnumValue<ProviderType> type in ProviderManager.GetProvidersThatAreAutoReplicating())
                        {
                            if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                                result = await DeleteAvatarByEmailForProviderAsync(email, result, SaveMode.AutoReplication, softDelete, type.Value);
                        }

                        if (result.WarningCount > 0)
                            ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with email ", email, " was successfully deleted for the provider ", previousProviderType, " but failed to auto-replicate for some of the other providers in the Auto-Replicate List. Providers in the list are: ", ProviderManager.GetProvidersThatAreAutoReplicatingAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)), true);
                        else
                            LoggingManager.Log("Avatar Successfully Deleted/Replicated", LogType.Info, ref result, true, false);
                    }
                }

                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured deleting the avatar with email ", email, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = false;
            }

            return result;
        }

        public OASISResult<bool> DeleteAvatarByEmail(string email, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            OASISResult<bool> result = new OASISResult<bool>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            ProviderType previousProviderType = ProviderType.Default;

            try
            {
                result = DeleteAvatarByEmailForProvider(email, result, SaveMode.FirstSaveAttempt, softDelete, providerType);
                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;

                if ((!result.Result || result.IsError) && ProviderManager.IsAutoFailOverEnabled)
                {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            result = DeleteAvatarByEmailForProvider(email, result, SaveMode.AutoFailOver, softDelete, type.Value);

                            if (!result.IsError && result.Result)
                            {
                                previousProviderType = ProviderManager.CurrentStorageProviderType.Value;
                                break;
                            }
                        }
                    }
                }

                if (result.IsError || !result.Result)
                    ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to delete the avatar with email ", email, ". Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                else
                {
                    result.IsSaved = true;

                    if (result.WarningCount > 0)
                        ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with email ", email, " was successfully deleted for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to delete for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
                    else
                        result.Message = "Avatar Successfully Deleted.";

                    //TODO: Need to move into background thread ASAP!
                    if (ProviderManager.IsAutoReplicationEnabled)
                    {
                        foreach (EnumValue<ProviderType> type in ProviderManager.GetProvidersThatAreAutoReplicating())
                        {
                            if (type.Value != previousProviderType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                                result = DeleteAvatarByEmailForProvider(email, result, SaveMode.AutoReplication, softDelete, type.Value);
                        }

                        if (result.WarningCount > 0)
                            ErrorHandling.HandleWarning(ref result, string.Concat("The avatar with email ", email, " was successfully deleted for the provider ", previousProviderType, " but failed to auto-replicate for some of the other providers in the Auto-Replicate List. Providers in the list are: ", ProviderManager.GetProvidersThatAreAutoReplicatingAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)), true);
                        else
                            LoggingManager.Log("Avatar Successfully Deleted/Replicated", LogType.Info, ref result, true, false);
                    }
                }

                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleError(ref result, string.Concat("Unknown error occured deleting the avatar with email ", email, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
                result.Result = false;
            }

            return result;
        }

        public async Task<KarmaAkashicRecord> AddKarmaToAvatarAsync(IAvatarDetail avatar, KarmaTypePositive karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType providerType = ProviderType.Default)
        {
            string errorMessage = "Error in AddKarmaToAvatarAsync method in AvatarManager.";
            OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
            OASISResult<KarmaAkashicRecord> result = new OASISResult<KarmaAkashicRecord>();

            if (providerResult != null && !providerResult.IsError && providerResult.Result != null)
            {
                result = await providerResult.Result.AddKarmaToAvatarAsync(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);

                if (result != null && !result.IsError && result.Result != null)
                {
                    result.Message = "Karma Successfully Added To Avatar.";
                    result.IsSaved = true;
                }
                else
                    ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {result.Message}");
            }
            else
                ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {providerResult.Message}");

            return result.Result;

            //TODO: Need to handle return of OASISResult properly..
            ////TODO: Need to implement Delete like HolonManager does to include error handling, auto replication, auto failed over, logging, etc....
            //return await ProviderManager.SetAndActivateCurrentStorageProvider(provider).Result.AddKarmaToAvatarAsync(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);
        }
        public async Task<KarmaAkashicRecord> AddKarmaToAvatarAsync(Guid avatarId, KarmaTypePositive karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType providerType = ProviderType.Default)
        {
            string errorMessage = "Error in AddKarmaToAvatarAsync method in AvatarManager.";
            OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
            OASISResult<KarmaAkashicRecord> result = new OASISResult<KarmaAkashicRecord>();

            if (providerResult != null && !providerResult.IsError && providerResult.Result != null)
            {
                OASISResult<IAvatarDetail> avatarResult = await providerResult.Result.LoadAvatarDetailAsync(avatarId);

                if (avatarResult != null && !avatarResult.IsError && avatarResult.Result != null)
                {
                    result = await providerResult.Result.AddKarmaToAvatarAsync(avatarResult.Result, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);

                    if (result != null && !result.IsError && result.Result != null)
                    {
                        result.Message = "Karma Successfully Added To Avatar.";
                        result.IsSaved = true;
                    }
                    else
                        ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {result.Message}");
                }
                else
                    ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: Avatar Not Found. Error Details: {avatarResult.Message}");
            }
            else
                ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {providerResult.Message}");

            return result.Result;

            //TODO: Need to handle return of OASISResult properly...
            //TODO: Need to implement Delete like HolonManager does to include error handling, auto replication, auto failed over, logging, etc...
            //IAvatarDetail avatar = ProviderManager.SetAndActivateCurrentStorageProvider(provider).Result.LoadAvatarDetail(avatarId);
            //return await ProviderManager.CurrentStorageProvider.AddKarmaToAvatarAsync(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);
        }

        public OASISResult<KarmaAkashicRecord> AddKarmaToAvatar(IAvatarDetail avatar, KarmaTypePositive karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType providerType = ProviderType.Default)
        {
            //TODO: Need to handle return of OASISResult properly...
            //TODO: Need to implement Delete like HolonManager does to include error handling, auto replication, auto failed over, logging, etc...
            return new OASISResult<KarmaAkashicRecord>(ProviderManager.SetAndActivateCurrentStorageProvider(providerType).Result.AddKarmaToAvatar(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink).Result);
        }
        public OASISResult<KarmaAkashicRecord> AddKarmaToAvatar(Guid avatarId, KarmaTypePositive karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType providerType = ProviderType.Default)
        {
            string errorMessage = "Error in AddKarmaToAvatar method in AvatarManager.";
            OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
            OASISResult<KarmaAkashicRecord> result = new OASISResult<KarmaAkashicRecord>();

            if (providerResult != null && !providerResult.IsError && providerResult.Result != null)
            {
                OASISResult<IAvatarDetail> avatarResult = providerResult.Result.LoadAvatarDetail(avatarId);

                if (avatarResult != null && !avatarResult.IsError && avatarResult.Result != null)
                {
                    result = providerResult.Result.AddKarmaToAvatar(avatarResult.Result, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);

                    if (result != null && !result.IsError && result.Result != null)
                    {
                        result.Message = "Karma Successfully Added To Avatar.";
                        result.IsSaved = true;
                    }
                    else
                        ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {result.Message}");
                }
                else
                    ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: Avatar Not Found. Error Details: {avatarResult.Message}");
            }
            else
                ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {providerResult.Message}");

            return result;

            /*
            //TODO: Need to handle return of OASISResult properly...
            OASISResult<KarmaAkashicRecord> result = new OASISResult<KarmaAkashicRecord>();
            OASISResult<IAvatarDetail> avatarResult = ProviderManager.SetAndActivateCurrentStorageProvider(provider).Result.LoadAvatarDetail(avatarId);
            //IAvatar avatar = ProviderManager.SetAndActivateCurrentStorageProvider(provider).Result.LoadAvatar(avatarId);

            if (avatarResult != null )
            {
                result.Result = ProviderManager.CurrentStorageProvider.AddKarmaToAvatar(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);

                if (result.Result != null)
                    result.Message = "Karma Successfully Added To Avatar.";
            }
            else
            {
                result.IsError = true;
                result.Message = "Avatar Not Found";
            }

            //TODO: Need to implement like avove and HolonManager does to include error handling, auto replication, auto failed over, logging, etc...

            return result;
            */
        }

        public async Task<KarmaAkashicRecord> RemoveKarmaFromAvatarAsync(IAvatarDetail avatar, KarmaTypeNegative karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType providerType = ProviderType.Default)
        {
            //TODO: Need to handle return of OASISResult properly...
            //TODO: Need to implement like avove and HolonManager does to include error handling, auto replication, auto failed over, logging, etc...
            //return await ProviderManager.SetAndActivateCurrentStorageProvider(provider).Result.RemoveKarmaFromAvatarAsync(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink).Result;

            string errorMessage = "Error in RemoveKarmaFromAvatarAsync method in AvatarManager.";
            OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
            OASISResult<KarmaAkashicRecord> result = new OASISResult<KarmaAkashicRecord>();

            if (providerResult != null && !providerResult.IsError && providerResult.Result != null)
            {
                result = await providerResult.Result.RemoveKarmaFromAvatarAsync(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);

                if (result != null && !result.IsError && result.Result != null)
                {
                    result.Message = "Karma Successfully Removed From Avatar.";
                    result.IsSaved = true;
                }
                else
                    ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {result.Message}");
            }
            else
                ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {providerResult.Message}");

            return result.Result;
        }

        public async Task<KarmaAkashicRecord> RemoveKarmaFromAvatarAsync(Guid avatarId, KarmaTypeNegative karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType providerType = ProviderType.Default)
        {
            string errorMessage = "Error in RemoveKarmaFromAvatarAsync method in AvatarManager.";
            OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
            OASISResult<KarmaAkashicRecord> result = new OASISResult<KarmaAkashicRecord>();

            if (providerResult != null && !providerResult.IsError && providerResult.Result != null)
            {
                OASISResult<IAvatarDetail> avatarResult = await providerResult.Result.LoadAvatarDetailAsync(avatarId);

                if (avatarResult != null && !avatarResult.IsError && avatarResult.Result != null)
                {
                    result = await providerResult.Result.RemoveKarmaFromAvatarAsync(avatarResult.Result, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);

                    if (result != null && !result.IsError && result.Result != null)
                    {
                        result.Message = "Karma Successfully Removed From Avatar.";
                        result.IsSaved = true;
                    }
                    else
                        ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {result.Message}");
                }
                else
                    ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: Avatar Not Found. Error Details: {avatarResult.Message}");
            }
            else
                ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {providerResult.Message}");

            return result.Result;

            //TODO: Need to handle return of OASISResult properly...
            //TODO: Need to implement like avove and HolonManager does to include error handling, auto replication, auto failed over, logging, etc...
            //IAvatarDetail avatar = ProviderManager.SetAndActivateCurrentStorageProvider(providerType).Result.LoadAvatarDetail(avatarId);
            //return await ProviderManager.CurrentStorageProvider.RemoveKarmaFromAvatarAsync(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);
        }

        public KarmaAkashicRecord RemoveKarmaFromAvatar(IAvatarDetail avatar, KarmaTypeNegative karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType providerType = ProviderType.Default)
        {
            //TODO: Need to handle return of OASISResult properly...
            //TODO: Need to implement like avove and HolonManager does to include error handling, auto replication, auto failed over, logging, etc...
            //return ProviderManager.SetAndActivateCurrentStorageProvider(provider).Result.RemoveKarmaFromAvatar(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);

            string errorMessage = "Error in RemoveKarmaFromAvatar method in AvatarManager.";
            OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
            OASISResult<KarmaAkashicRecord> result = new OASISResult<KarmaAkashicRecord>();

            if (providerResult != null && !providerResult.IsError && providerResult.Result != null)
            {
                result = providerResult.Result.RemoveKarmaFromAvatar(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);

                if (result != null && !result.IsError && result.Result != null)
                {
                    result.Message = "Karma Successfully Removed From Avatar.";
                    result.IsSaved = true;
                }
                else
                    ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {result.Message}");
            }
            else
                ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {providerResult.Message}");

            return result.Result;
        }

        public OASISResult<KarmaAkashicRecord> RemoveKarmaFromAvatar(Guid avatarId, KarmaTypeNegative karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType providerType = ProviderType.Default)
        {
            string errorMessage = "Error in RemoveKarmaFromAvatar method in AvatarManager.";
            OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
            OASISResult<KarmaAkashicRecord> result = new OASISResult<KarmaAkashicRecord>();

            if (providerResult != null && !providerResult.IsError && providerResult.Result != null)
            {
                OASISResult<IAvatarDetail> avatarResult = providerResult.Result.LoadAvatarDetail(avatarId);

                if (avatarResult != null && !avatarResult.IsError && avatarResult.Result != null)
                {
                    result = providerResult.Result.RemoveKarmaFromAvatar(avatarResult.Result, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);

                    if (result != null && !result.IsError && result.Result != null)
                    {
                        result.Message = "Karma Successfully Removed From Avatar.";
                        result.IsSaved = true;
                    }
                    else
                        ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {result.Message}");
                }
                else
                    ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: Avatar Not Found. Error Details: {avatarResult.Message}");
            }
            else
                ErrorHandling.HandleError(ref result, $"{errorMessage} Reason: {providerResult.Message}");

            return result;
        }

        //public bool CheckIfEmailIsAlreadyInUse(string email)
        //{
        //    OASISResult<IAvatar> result = LoadAvatarByEmail(email);

        //    if (!result.IsError && result.Result != null)
        //    {
        //        //If the avatar has previously been deleted (soft deleted) then allow them to create a new avatar with the same email address.
        //       // if (result.Result.DeletedDate == DateTime.MinValue)
        //            return true;
        //      //  else
        //     //       return false;
        //    }
        //    else
        //        return false;
        //}

        private IAvatar PrepareAvatarForSaving(IAvatar avatar)
        {
            if (string.IsNullOrEmpty(avatar.Username))
                avatar.Username = avatar.Email;

            if (avatar.Id == Guid.Empty || avatar.CreatedDate == DateTime.MinValue)
            {
                if (avatar.Id == Guid.Empty)
                    avatar.Id = Guid.NewGuid();

                avatar.IsNewHolon = true;
            }
            else if (avatar.CreatedDate != DateTime.MinValue)
                avatar.IsNewHolon = false;

            // TODO: I think it's best to include audit stuff here so the providers do not need to worry about it?
            // Providers could always override this behaviour if they choose...
            if (!avatar.IsNewHolon)
            {
                avatar.ModifiedDate = DateTime.Now;

                if (LoggedInAvatar != null)
                    avatar.ModifiedByAvatarId = LoggedInAvatar.Id;

                avatar.Version++;
                avatar.PreviousVersionId = avatar.VersionId;
                avatar.VersionId = Guid.NewGuid();
            }
            else
            {
                avatar.IsActive = true;
                avatar.CreatedDate = DateTime.Now;

                if (LoggedInAvatar != null)
                    avatar.CreatedByAvatarId = LoggedInAvatar.Id;

                avatar.Version = 1;
                avatar.VersionId = Guid.NewGuid();
            }

            return avatar;
        }

        private IAvatarDetail PrepareAvatarDetailForSaving(IAvatarDetail avatar)
        {
            //if (string.IsNullOrEmpty(avatar.Username))
            //    avatar.Username = avatar.Email;

            if (avatar.Id == Guid.Empty)
            {
                avatar.Id = Guid.NewGuid();
                avatar.IsNewHolon = true;
            }
            else if (avatar.CreatedDate != DateTime.MinValue)
                avatar.IsNewHolon = false;

            // TODO: I think it's best to include audit stuff here so the providers do not need to worry about it?
            // Providers could always override this behaviour if they choose...
            if (!avatar.IsNewHolon)
            {
                avatar.ModifiedDate = DateTime.Now;

                if (LoggedInAvatar != null)
                    avatar.ModifiedByAvatarId = LoggedInAvatar.Id;
            }
            else
            {
                avatar.IsActive = true;
                avatar.CreatedDate = DateTime.Now;

                if (LoggedInAvatar != null)
                    avatar.CreatedByAvatarId = LoggedInAvatar.Id;
            }

            return avatar;
        }

        private string GenerateJWTToken(IAvatar account)
        {
            //TODO: Replace exception with OASISResult ASAP.
            if (string.IsNullOrEmpty(OASISDNA.OASIS.Security.SecretKey))
                throw new ArgumentNullException("OASISDNA.OASIS.Security.SecretKey", "OASISDNA.OASIS.Security.SecretKey is missing, please generate a unique secret key from two GUID's.");

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(OASISDNA.OASIS.Security.SecretKey);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim("id", account.Id.ToString()) }),
                Expires = DateTime.UtcNow.AddMinutes(15),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private RefreshToken generateRefreshToken(string ipAddress)
        {
            return new RefreshToken
            {
                Token = randomTokenString(),
                Expires = DateTime.UtcNow.AddDays(7),
                Created = DateTime.UtcNow,
                CreatedByIp = ipAddress
            };
        }

        private string randomTokenString()
        {
            using var rngCryptoServiceProvider = new RNGCryptoServiceProvider();
            var randomBytes = new byte[40];
            rngCryptoServiceProvider.GetBytes(randomBytes);
            // convert random bytes to hex string
            return BitConverter.ToString(randomBytes).Replace("-", "");
        }

        public IAvatar HideAuthDetails(IAvatar avatar, bool hidePassword = true, bool hidePrivateKeys = true, bool hideVerificationToken = true, bool hideRefreshTokens = true)
        {
            if (OASISDNA.OASIS.Security.HideVerificationToken || hideVerificationToken)
                avatar.VerificationToken = null;

            if (hidePassword)
                avatar.Password = null;

            if (hidePrivateKeys)
                avatar.ProviderPrivateKey = null;

            if (OASISDNA.OASIS.Security.HideRefreshTokens || hideRefreshTokens)
                avatar.RefreshTokens = null;

            return avatar;
        }

        public IEnumerable<IAvatar> HideAuthDetails(IEnumerable<IAvatar> avatars)
        {
            List<IAvatar> tempList = avatars.ToList();

            for (int i = 0; i < tempList.Count; i++)
                tempList[i] = HideAuthDetails(tempList[i]);

            return tempList;
        }

        private void sendAlreadyRegisteredEmail(string email, string origin)
        {
            string message;

            if (!string.IsNullOrEmpty(origin))
                message = $@"<p>If you don't know your password please visit the <a href=""{origin}/avatar/forgot-password"">forgot password</a> page.</p>";
            else
                message = "<p>If you don't know your password you can reset it via the <code>/avatar/forgot-password</code> api route.</p>";

            if (!EmailManager.IsInitialized)
                EmailManager.Initialize(OASISDNA);

            EmailManager.Send(
                to: email,
                subject: "OASIS Sign-up Verification - Email Already Registered",
                html: $@"<h4>Email Already Registered</h4>
                         <p>Your email <strong>{email}</strong> is already registered.</p>
                         {message}"
            );
        }

        private void sendVerificationEmail(IAvatar avatar, string origin)
        {
            string message;

            if (!string.IsNullOrEmpty(origin))
            {
                var verifyUrl = $"{origin}/avatar/verify-email?token={avatar.VerificationToken}";
                message = $@"<p>Please click the below link to verify your email address:</p>
                             <p><a href=""{verifyUrl}"">{verifyUrl}</a></p>";
            }
            else
            {
                message = $@"<p>Please use the below token to verify your email address with the <code>/avatar/verify-email</code> api route:</p>
                             <p><code>{avatar.VerificationToken}</code></p>";
            }

            if (!EmailManager.IsInitialized)
                EmailManager.Initialize(OASISDNA);

            EmailManager.Send(
                to: avatar.Email,
                subject: "OASIS Sign-up Verification - Verify Email",
                //html: $@"<h4>Verify Email</h4>
                html: $@"<h4>Verify Email</h4>
                         <p>Thanks for registering!</p>
                         <p>Welcome to the OASIS!</p>
                         <p>Ready Player One?</p>
                         {message}"
            );
        }

        private OASISResult<IAvatar> PrepareToRegisterAvatar(string avatarTitle, string firstName, string lastName, string email, string password, AvatarType avatarType, string origin, OASISType createdOASISType)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();

            if (!ValidationHelper.IsValidEmail(email))
            {
                result.IsError = true;
                result.Message = "The email is not valid.";
                return result;
            }

            OASISResult<IAvatar> existingAvatarResult = LoadAvatarByEmail(email);

            if (!existingAvatarResult.IsError && existingAvatarResult.Result != null)
            {
                //If the avatar has previously been deleted (soft deleted) then allow them to create a new avatar with the same email address.
                if (existingAvatarResult.Result.DeletedDate != DateTime.MinValue)
                {
                    sendAlreadyRegisteredEmail(email, origin);
                    ErrorHandling.HandleError(ref result, $"This avatar was deleted on {existingAvatarResult.Result.DeletedDate} by avatar with id {existingAvatarResult.Result.DeletedByAvatarId}, please contact support or create a new avatar with a new email address.");
                    return result;
                }
                else
                {
                    sendAlreadyRegisteredEmail(email, origin);
                    ErrorHandling.HandleError(ref result, "Avatar Already Registered.");
                    return result;
                }
            }

            //TODO: {PERFORMANCE} Add this method to the providers so more efficient.
            //if (CheckIfEmailIsAlreadyInUse(email))
            //{
            //    // send already registered error in email to prevent account enumeration
            //    sendAlreadyRegisteredEmail(email, origin);
            //    result.IsError = true;
            //    result.Message = "Avatar Already Registered.";
            //    return result;
            //}

            result.Result = new Avatar() { Id = Guid.NewGuid(), IsNewHolon = true, FirstName = firstName, LastName = lastName, Password = password, Title = avatarTitle, Email = email, AvatarType = new EnumValue<AvatarType>(avatarType), CreatedOASISType = new EnumValue<OASISType>(createdOASISType) };
            result.Result.Username = result.Result.Email; //Default the username to their email (they can change this later in Avatar Profile screen).

            //result.Result.CreatedDate = DateTime.UtcNow;
            result.Result.VerificationToken = randomTokenString();

            // hash password
            result.Result.Password = BC.HashPassword(password);

            return result;
        }

        private OASISResult<IAvatarDetail> PrepareToRegisterAvatarDetail(Guid avatarId, string username, string email, OASISType createdOASISType, ConsoleColor cliColour = ConsoleColor.Green, ConsoleColor favColour = ConsoleColor.Green)
        {
            OASISResult<IAvatarDetail> result = new OASISResult<IAvatarDetail>();
            IAvatarDetail avatarDetail = new AvatarDetail() { Id = avatarId, IsNewHolon = true, Email = email, Username = username, CreatedOASISType = new EnumValue<OASISType>(createdOASISType), STARCLIColour = cliColour, FavouriteColour = favColour };

            // TODO: Temp! Remove later!
            if (email == "davidellams@hotmail.com")
            {
                avatarDetail.Karma = 777777;
                avatarDetail.XP = 2222222;

                avatarDetail.GeneKeys.Add(new GeneKey() { Name = "Expectation", Gift = "a gift", Shadow = "a shadow", Sidhi = "a sidhi" });
                avatarDetail.GeneKeys.Add(new GeneKey() { Name = "Invisibility", Gift = "a gift", Shadow = "a shadow", Sidhi = "a sidhi" });
                avatarDetail.GeneKeys.Add(new GeneKey() { Name = "Rapture", Gift = "a gift", Shadow = "a shadow", Sidhi = "a sidhi" });

                avatarDetail.HumanDesign.Type = "Generator";
                avatarDetail.Inventory.Add(new InventoryItem() { Name = "Magical Armour" });
                avatarDetail.Inventory.Add(new InventoryItem() { Name = "Mighty Wizard Sword" });

                avatarDetail.Spells.Add(new Spell() { Name = "Super Spell" });
                avatarDetail.Spells.Add(new Spell() { Name = "Super Speed Spell" });
                avatarDetail.Spells.Add(new Spell() { Name = "Super Srength Spell" });

                avatarDetail.Achievements.Add(new Achievement() { Name = "Becoming Superman!" });
                avatarDetail.Achievements.Add(new Achievement() { Name = "Completing STAR!" });

                avatarDetail.Gifts.Add(new AvatarGift() { GiftType = KarmaTypePositive.BeASuperHero });

                avatarDetail.Aura.Brightness = 99;
                avatarDetail.Aura.Level = 77;
                avatarDetail.Aura.Progress = 88;
                avatarDetail.Aura.Size = 10;
                avatarDetail.Aura.Value = 777;

                avatarDetail.Chakras.Root.Level = 77;
                avatarDetail.Chakras.Root.Progress = 99;
                avatarDetail.Chakras.Root.XP = 8783;

                avatarDetail.Attributes.Dexterity = 99;
                avatarDetail.Attributes.Endurance = 99;
                avatarDetail.Attributes.Intelligence = 99;
                avatarDetail.Attributes.Magic = 99;
                avatarDetail.Attributes.Speed = 99;
                avatarDetail.Attributes.Strength = 99;
                avatarDetail.Attributes.Toughness = 99;
                avatarDetail.Attributes.Vitality = 99;
                avatarDetail.Attributes.Wisdom = 99;

                avatarDetail.Stats.Energy.Current = 99;
                avatarDetail.Stats.Energy.Max = 99;
                avatarDetail.Stats.HP.Current = 99;
                avatarDetail.Stats.HP.Max = 99;
                avatarDetail.Stats.Mana.Current = 99;
                avatarDetail.Stats.Mana.Max = 99;
                avatarDetail.Stats.Staminia.Current = 99;
                avatarDetail.Stats.Staminia.Max = 99;

                avatarDetail.SuperPowers.AstralProjection = 99;
                avatarDetail.SuperPowers.BioLocatation = 88;
                avatarDetail.SuperPowers.Flight = 99;
                avatarDetail.SuperPowers.FreezeBreath = 88;
                avatarDetail.SuperPowers.HeatVision = 99;
                avatarDetail.SuperPowers.Invulerability = 99;
                avatarDetail.SuperPowers.SuperSpeed = 99;
                avatarDetail.SuperPowers.SuperStrength = 99;
                avatarDetail.SuperPowers.XRayVision = 99;
                avatarDetail.SuperPowers.Teleportation = 99;
                avatarDetail.SuperPowers.Telekineseis = 99;

                avatarDetail.Skills.Computers = 99;
                avatarDetail.Skills.Engineering = 99;
            }

            //avatarDetail.CreatedDate = DateTime.UtcNow;

            result.Result = avatarDetail;
            return result;
        }

        private OASISResult<IAvatar> AvatarRegistered(OASISResult<IAvatar> result, string origin)
        {
            if (OASISDNA.OASIS.Email.SendVerificationEmail)
                sendVerificationEmail(result.Result, origin);

            result.Result = HideAuthDetails(result.Result);
            result.IsSaved = true;
            result.Message = "Avatar Created Successfully. Please check your email for the verification email. You will not be able to log in till you have verified your email. Thank you.";

            return result;
        }

        /*
        //TODO: Want to try and get all methods above to route through some generic function like this ASAP...
        private async Task<OASISResult<IAvatar>> LoadAvatarAsync(Func<string, int, Task<OASISResult<IAvatar>>> avatarLoadFunc, string param1, bool hideAuthDetails = true, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;

            result = await LoadAvatarForProviderAsync(avatarLoadFunc, param1, providerType, version);

            if (result.Result == null && ProviderManager.IsAutoFailOverEnabled)
            {
                foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                {
                    if (type.Value != providerType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                    {
                        result = await LoadAvatarForProviderAsync(avatarLoadFunc, param1, type.Value, version);

                        if (!result.IsError && result.Result != null)
                            break;
                    }
                }
            }

            if (result.Result == null)
                ErrorHandling.HandleError(ref result, String.Concat("All registered OASIS Providers in the AutoFailOverList failed to load avatar, ", param1, ". Please view the logs or DetailedMessage property for more information. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages)));
            else
            {
                if (result.WarningCount > 0)
                    result.Message = string.Concat("The avatar ", param1, " loaded successfully for the provider ", ProviderManager.CurrentStorageProviderType.Value, " but failed to load for some of the other providers in the AutoFailOverList. Providers in the list are: ", ProviderManager.GetProviderAutoFailOverListAsString()), string.Concat("Error Message: ", OASISResultHelper.BuildInnerMessageError(result.InnerMessages));

                if (hideAuthDetails)
                    result.Result = HideAuthDetails(result.Result);
            }

            // Set the current provider back to the original provider.
            ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);

            return result;
        }

        private async Task<OASISResult<IAvatar>> LoadAvatarForProviderAsync(Func<string, int, Task<OASISResult<IAvatar>>> avatarLoadFunc, string param1, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = avatarLoadFunc(param1, version);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds)) == task)
                    {
                        result = task.Result;

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message))
                                result.Message = "Avatar Not Found.";

                            ErrorHandling.HandleWarning(ref result, string.Concat("Error loading avatar ", param1, " for provider ", ProviderManager.CurrentStorageProviderType.Name, ". Reason: ", result.Message));
                        }
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat("Error loading avatar ", param1, " for provider ", ProviderManager.CurrentStorageProviderType.Name, ". Reason: timeout occured."));
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat("Error loading avatar ", param1, " for provider ", ProviderManager.CurrentStorageProviderType.Name, ". There was an error setting the provider. Reason:", providerResult.Message));
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat("Unknown error occured loading avatar ", param1, " for provider ", ProviderManager.CurrentStorageProviderType.Name), string.Concat("Error Message: ", ex.Message), ex);
            }

            return result;
        }*/

        private OASISResult<IAvatar> LoadAvatarForProvider(Guid id, OASISResult<IAvatar> result,  ProviderType providerType = ProviderType.Default, int version = 0)
        {
            //TODO: IMPLEMENT DIFFERENT TIMEOUT MECHNISM FOR NON-ASYNC METHODS? OR JUST CALL THE ASYNC VERSION?
            return LoadAvatarForProviderAsync(id, result, providerType, version).Result;
        }

        private async Task<OASISResult<IAvatar>> LoadAvatarForProviderAsync(Guid id, OASISResult<IAvatar> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            //string errorMessageTemplate = "Error in LoadAvatarForProviderAsync method in AvatarManager loading avatar with id {id} for provider {providerType}. Reason: ";
            string errorMessageTemplate = "Error in LoadAvatarForProviderAsync method in AvatarManager loading avatar with id {0} for provider {1}. Reason: ";
            string errorMessage = String.Format(errorMessageTemplate, id, providerType);

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = String.Format(errorMessageTemplate, id, ProviderManager.CurrentStorageProviderType.Name);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.LoadAvatarAsync(id, version);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        result = task.Result;

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message))
                                result.Message = "Avatar Not Found.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message));
                        }
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."));
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message));
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex);
            }

            return result;
        }

        private OASISResult<IAvatar> LoadAvatarForProvider(string username, OASISResult<IAvatar> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            //TODO: IMPLEMENT DIFFERENT TIMEOUT MECHNISM FOR NON-ASYNC METHODS? OR JUST CALL THE ASYNC VERSION?
            return LoadAvatarForProviderAsync(username, result, providerType, version).Result;
        }

        private async Task<OASISResult<IAvatar>> LoadAvatarForProviderAsync(string username, OASISResult<IAvatar> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            //string errorMessage = $"Error in LoadAvatarForProviderAsync method in AvatarManager loading avatar with username {username} for provider {providerType}. Reason: ";
            string errorMessageTemplate = "Error in LoadAvatarForProviderAsync method in AvatarManager loading avatar with username {0} for provider {1}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, username, providerType);

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, username, ProviderManager.CurrentStorageProviderType.Name);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.LoadAvatarAsync(username, version);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        result = task.Result;

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message))
                                result.Message = "Avatar Not Found.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message));
                        }
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."));
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message));
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex);
            }

            return result;
        }

        private OASISResult<IAvatar> LoadAvatarForProvider(string username, string password, OASISResult<IAvatar> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            return LoadAvatarForProviderAsync(username, password, result).Result;
        }

        private async Task<OASISResult<IAvatar>> LoadAvatarForProviderAsync(string username, string password, OASISResult<IAvatar> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            //string errorMessage = $"Error in LoadAvatarForProviderAsync method in AvatarManager loading avatar with username {username} for provider {providerType}. Reason: ";
            string errorMessageTemplate = "Error in LoadAvatarForProviderAsync method in AvatarManager loading avatar with username {0} for provider {1}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, username, providerType);

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, username, ProviderManager.CurrentStorageProviderType.Name);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.LoadAvatarAsync(username, password, version);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        result = task.Result;

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message))
                                result.Message = "Avatar Not Found.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message));
                        }
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."));
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message));
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex);
            }

            return result;
        }

        private OASISResult<IAvatar> LoadAvatarByEmailForProvider(string email, OASISResult<IAvatar> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            return LoadAvatarByEmailForProviderAsync(email, result, providerType, version).Result;
        }

        private async Task<OASISResult<IAvatar>> LoadAvatarByEmailForProviderAsync(string email, OASISResult<IAvatar> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            //string errorMessage = $"Error in LoadAvatarByEmailForProviderAsync method in AvatarManager loading avatar with email {email} for provider {providerType}. Reason: ";
            string errorMessageTemplate = "Error in LoadAvatarByEmailForProviderAsync method in AvatarManager loading avatar with email {0} for provider {1}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, email, providerType);

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, email, ProviderManager.CurrentStorageProviderType.Name);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.LoadAvatarByEmailAsync(email, version);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        result = task.Result;

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message))
                                result.Message = "Avatar Not Found.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message));
                        }
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."));
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message));
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex);
            }

            return result;
        }

        private OASISResult<IAvatarDetail> LoadAvatarDetailForProvider(Guid id, OASISResult<IAvatarDetail> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            return LoadAvatarDetailForProviderAsync(id, result, providerType, version).Result;
        }

        private async Task<OASISResult<IAvatarDetail>> LoadAvatarDetailForProviderAsync(Guid id, OASISResult<IAvatarDetail> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            string errorMessageTemplate = "Error in LoadAvatarDetailForProviderAsync method in AvatarManager loading avatar detail with id {0} for provider {1}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, id, providerType);
            //string errorMessage = $"Error in LoadAvatarDetailForProviderAsync method in AvatarManager loading avatar detail with id {id} for provider {providerType}. Reason: ";

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, id, providerType);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.LoadAvatarDetailAsync(id, version);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        result = task.Result;

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message))
                                result.Message = "Avatar Detail Not Found.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message));
                        }
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."));
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message));
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex);
            }

            return result;
        }

        private OASISResult<IAvatarDetail> LoadAvatarDetailByEmailForProvider(string email, OASISResult<IAvatarDetail> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            return LoadAvatarDetailByEmailForProviderAsync(email, result, providerType, version).Result;
        }

        private async Task<OASISResult<IAvatarDetail>> LoadAvatarDetailByEmailForProviderAsync(string email, OASISResult<IAvatarDetail> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            //string errorMessage = $"Error in LoadAvatarDetailByUsernameForProviderAsync method in AvatarManager loading avatar detail with email {email} for provider {providerType}. Reason: ";
            string errorMessageTemplate = "Error in LoadAvatarDetailByEmailForProviderAsync method in AvatarManager loading avatar detail with email {0} for provider {1}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, email, providerType);

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, email, ProviderManager.CurrentStorageProviderType.Name);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.LoadAvatarDetailByEmailAsync(email, version);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        result = task.Result;

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message))
                                result.Message = "Avatar Detail Not Found.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message));
                        }
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."));
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message));
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex);
            }

            return result;
        }

        private OASISResult<IAvatarDetail> LoadAvatarDetailByUsernameForProvider(string username, OASISResult<IAvatarDetail> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            return LoadAvatarDetailByUsernameForProviderAsync(username, result, providerType, version).Result;
        }

        private async Task<OASISResult<IAvatarDetail>> LoadAvatarDetailByUsernameForProviderAsync(string username, OASISResult<IAvatarDetail> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            //string errorMessage = $"Error in LoadAvatarDetailByUsernameForProviderAsync method in AvatarManager loading avatar detail with username {username} for provider {providerType}. Reason: ";
            string errorMessageTemplate = "Error in LoadAvatarDetailByUsernameForProviderAsync method in AvatarManager loading avatar detail with username {0} for provider {1}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, username, providerType);

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, username, ProviderManager.CurrentStorageProviderType.Name);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.LoadAvatarDetailByUsernameAsync(username, version);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        result = task.Result;

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message))
                                result.Message = "Avatar Detail Not Found.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message));
                        }
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."));
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message));
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex);
            }

            return result;
        }

        private OASISResult<IEnumerable<IAvatar>> LoadAllAvatarsForProvider(OASISResult<IEnumerable<IAvatar>> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            return LoadAllAvatarsForProviderAsync(result, providerType, version).Result;
        }

        private async Task<OASISResult<IEnumerable<IAvatar>>> LoadAllAvatarsForProviderAsync(OASISResult<IEnumerable<IAvatar>> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            //string errorMessage = $"Error in LoadAllAvatarsForProviderAsync method in AvatarManager loading all avatars for provider {providerType}. Reason: ";
            string errorMessageTemplate = "Error in LoadAllAvatarsForProviderAsync method in AvatarManager loading all avatar details for provider {0}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, providerType);

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, ProviderManager.CurrentStorageProviderType.Name);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.LoadAllAvatarsAsync(version);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        result = task.Result;

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message))
                                result.Message = "No Avatars Were Found.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message));
                        }
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."));
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message));
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex);
            }

            return result;
        }

        private OASISResult<IEnumerable<IAvatarDetail>> LoadAllAvatarDetailsForProvider(OASISResult<IEnumerable<IAvatarDetail>> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            return LoadAllAvatarDetailsForProviderAsync(result, providerType, version).Result;
        }

        private async Task<OASISResult<IEnumerable<IAvatarDetail>>> LoadAllAvatarDetailsForProviderAsync(OASISResult<IEnumerable<IAvatarDetail>> result, ProviderType providerType = ProviderType.Default, int version = 0)
        {
            //string errorMessage = $"Error in LoadAllAvatarDetailsForProviderAsync method in AvatarManager loading all avatar details for provider {providerType}. Reason: ";
            string errorMessageTemplate = "Error in LoadAllAvatarDetailsForProviderAsync method in AvatarManager loading all avatar details for provider {0}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, providerType);

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, ProviderManager.CurrentStorageProviderType.Name);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.LoadAllAvatarDetailsAsync(version);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        result = task.Result;

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message))
                                result.Message = "No Avatar Details Were Found.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message));
                        }
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."));
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message));
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message));
            }

            return result;
        }

        public async Task<OASISResult<IAvatar>> SaveAvatarForProviderAsync(IAvatar avatar, OASISResult<IAvatar> result, SaveMode saveMode, ProviderType providerType = ProviderType.Default)
        {
            //ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            //string errorMessage = $"Error in SaveAvatarDetailForProviderAsync method in AvatarManager saving avatar with name {avatar.Name}, username {avatar.Username} and id {avatar.Id} for provider {providerType}. Reason: ";
            string errorMessageTemplate = "Error in SaveAvatarDetailForProviderAsync method in AvatarManager saving avatar with name {0}, username {1} and id {2} for provider {3} for {4}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, avatar.Name, avatar.Username, avatar.Id, providerType, Enum.GetName(typeof(SaveMode), saveMode));

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, avatar.Name, avatar.Username, avatar.Id, ProviderManager.CurrentStorageProviderType.Name, Enum.GetName(typeof(SaveMode), saveMode));

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.SaveAvatarAsync(avatar);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        if (saveMode != SaveMode.AutoReplication)
                            result = task.Result;
                        else
                            OASISResultHolonToHolonHelper<IAvatar, IAvatar>.CopyResult(task.Result, result, false);

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message) && saveMode != SaveMode.AutoReplication)
                                result.Message = "Unknown.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message), saveMode == SaveMode.AutoReplication);
                        }
                        else
                            result.IsSaved = true;
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."), saveMode == SaveMode.AutoReplication);
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message), saveMode == SaveMode.AutoReplication);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex, saveMode == SaveMode.AutoReplication);
            }

            return result;
        }

        public OASISResult<IAvatar> SaveAvatarForProvider(IAvatar avatar, OASISResult<IAvatar> result, SaveMode saveMode, ProviderType providerType = ProviderType.Default)
        {
            //ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            //string errorMessage = $"Error in SaveAvatarForProvider method in AvatarManager saving avatar with name {avatar.Name}, username {avatar.Username} and id {avatar.Id} for provider {providerType} for {Enum.GetName(typeof(SaveMode), saveMode)}. Reason: ";
            string errorMessageTemplate = "Error in SaveAvatarForProvider method in AvatarManager saving avatar detail with name {0}, username {1} and id {2} for provider {3} for {4}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, avatar.Name, avatar.Username, avatar.Id, providerType, Enum.GetName(typeof(SaveMode), saveMode));

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, avatar.Name, avatar.Username, avatar.Id, ProviderManager.CurrentStorageProviderType.Name, Enum.GetName(typeof(SaveMode), saveMode));

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = Task.Run(() => providerResult.Result.SaveAvatar(avatar));

                    if (task.Wait(TimeSpan.FromSeconds(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds)))
                    {
                        if (saveMode != SaveMode.AutoReplication)
                            result = task.Result;
                        else
                            OASISResultHolonToHolonHelper<IAvatar, IAvatar>.CopyResult(task.Result, result, false);

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message) && saveMode != SaveMode.AutoReplication)
                                result.Message = "Unknown.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message), saveMode == SaveMode.AutoReplication);
                        }
                        else
                            result.IsSaved = true;
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."), saveMode == SaveMode.AutoReplication);
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message), saveMode == SaveMode.AutoReplication);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex, saveMode == SaveMode.AutoReplication);
            }

            return result;
        }

        public async Task<OASISResult<IAvatarDetail>> SaveAvatarDetailForProviderAsync(IAvatarDetail avatar, OASISResult<IAvatarDetail> result, SaveMode saveMode, ProviderType providerType = ProviderType.Default)
        {
            //ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            string errorMessageTemplate = "Error in SaveAvatarDetailForProviderAsync method in AvatarManager saving avatar detail with name {0}, username {1} and id {2} for provider {3} for {4}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, avatar.Name, avatar.Username, avatar.Id, providerType, Enum.GetName(typeof(SaveMode), saveMode));
            //string errorMessage = $"Error in SaveAvatarDetailForProviderAsync method in AvatarManager saving avatar detail with name {avatar.Name}, username {avatar.Username} and id {avatar.Id} for provider {providerType} for {Enum.GetName(typeof(SaveMode), saveMode)}. Reason: ";

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, avatar.Name, avatar.Username, avatar.Id, ProviderManager.CurrentStorageProviderType.Name, Enum.GetName(typeof(SaveMode), saveMode));

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.SaveAvatarDetailAsync(avatar);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        if (saveMode != SaveMode.AutoReplication)
                            result = task.Result;
                        else
                            OASISResultHolonToHolonHelper<IAvatarDetail, IAvatarDetail>.CopyResult(task.Result, result, false);

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message) && saveMode != SaveMode.AutoReplication)
                                result.Message = "Unknown.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message), saveMode == SaveMode.AutoReplication);
                        }
                        else
                            result.IsSaved = true;
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."), saveMode == SaveMode.AutoReplication);
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message), saveMode == SaveMode.AutoReplication);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex, saveMode == SaveMode.AutoReplication);
            }

            return result;
        }

        public OASISResult<IAvatarDetail> SaveAvatarDetailForProvider(IAvatarDetail avatar, OASISResult<IAvatarDetail> result, SaveMode saveMode, ProviderType providerType = ProviderType.Default)
        {
            return SaveAvatarDetailForProviderAsync(avatar, result, saveMode, providerType).Result;
        }

        public OASISResult<bool> DeleteAvatarForProvider(Guid id, OASISResult<bool> result, SaveMode saveMode, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            return DeleteAvatarForProviderAsync(id, result, saveMode, softDelete, providerType).Result;
        }

        public async Task<OASISResult<bool>> DeleteAvatarForProviderAsync(Guid id, OASISResult<bool> result, SaveMode saveMode, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            string errorMessageTemplate = "Error in DeleteAvatarForProviderAsync method in AvatarManager deleting avatar with email {0} for provider {1} for {2}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, id, providerType, Enum.GetName(typeof(SaveMode), saveMode));
            //string errorMessage = $"Error in DeleteAvatarForProviderAsync method in AvatarManager deleting avatar with id {id} for provider {providerType} for {Enum.GetName(typeof(SaveMode), saveMode)}. Reason: ";

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, id, ProviderManager.CurrentStorageProviderType.Name, Enum.GetName(typeof(SaveMode), saveMode));

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.DeleteAvatarAsync(id, softDelete);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        if (saveMode != SaveMode.AutoReplication)
                            result = task.Result;
                        else
                            OASISResultHolonToHolonHelper<bool, bool>.CopyResult(task.Result, result, false);

                        if (result.IsError || !result.Result)
                        {
                            if (string.IsNullOrEmpty(result.Message) && saveMode != SaveMode.AutoReplication)
                                result.Message = "Unknown.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message), saveMode == SaveMode.AutoReplication);
                        }
                        else
                            result.IsSaved = true;
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."), saveMode == SaveMode.AutoReplication);
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message), saveMode == SaveMode.AutoReplication);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex, saveMode == SaveMode.AutoReplication);
            }

            return result;
        }

        public OASISResult<bool> DeleteAvatarByEmailForProvider(string email, OASISResult<bool> result, SaveMode saveMode, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            return DeleteAvatarByEmailForProviderAsync(email, result, saveMode, softDelete, providerType).Result;
        }

        public async Task<OASISResult<bool>> DeleteAvatarByEmailForProviderAsync(string email, OASISResult<bool> result, SaveMode saveMode, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            //ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            //string errorMessage = $"Error in DeleteAvatarByEmailForProviderAsync method in AvatarManager deleting avatar with email {email} for provider {providerType} for {Enum.GetName(typeof(SaveMode), saveMode)}. Reason: ";
            string errorMessageTemplate = "Error in DeleteAvatarByEmailForProviderAsync method in AvatarManager deleting avatar with email {0} for provider {1} for {2}. Reason: ";
            string errorMessage = string.Format(errorMessageTemplate, email, providerType, Enum.GetName(typeof(SaveMode), saveMode));

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);
                errorMessage = string.Format(errorMessageTemplate, email, ProviderManager.CurrentStorageProviderType.Name, Enum.GetName(typeof(SaveMode), saveMode));

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.DeleteAvatarAsync(email, softDelete);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        if (saveMode != SaveMode.AutoReplication)
                            result = task.Result;
                        else
                            OASISResultHolonToHolonHelper<bool, bool>.CopyResult(task.Result, result, false);

                        if (result.IsError || !result.Result)
                        {
                            if (string.IsNullOrEmpty(result.Message) && saveMode != SaveMode.AutoReplication)
                                result.Message = "Unknown.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message), saveMode == SaveMode.AutoReplication);
                        }
                        else
                            result.IsSaved = true;
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."), saveMode == SaveMode.AutoReplication);
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message), saveMode == SaveMode.AutoReplication);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex, saveMode == SaveMode.AutoReplication);
            }

            return result;
        }

        public OASISResult<bool> DeleteAvatarByUsernameForProvider(string username, OASISResult<bool> result, SaveMode saveMode, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            return DeleteAvatarByUsernameForProviderAsync(username, result, saveMode, softDelete, providerType).Result;
        }

        public async Task<OASISResult<bool>> DeleteAvatarByUsernameForProviderAsync(string username, OASISResult<bool> result, SaveMode saveMode, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            //ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            //string errorMessage = $"Error in DeleteAvatarByUsernameForProviderAsync method in AvatarManager deleting avatar with username {username} for provider {providerType} for {Enum.GetName(typeof(SaveMode), saveMode)}. Reason: ";
            string errorMessageTemplate = "Error in DeleteAvatarByUsernameForProviderAsync method in AvatarManager deleting avatar with username {0} for provider {1} for {2}. Reason: ";
            string errorMessage = String.Format(errorMessageTemplate, username, providerType, Enum.GetName(typeof(SaveMode), saveMode));

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.DeleteAvatarAsync(username, softDelete);
                    errorMessage = String.Format(errorMessageTemplate, username, ProviderManager.CurrentStorageProviderType.Name, Enum.GetName(typeof(SaveMode), saveMode));

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds * 1000)) == task)
                    {
                        if (saveMode != SaveMode.AutoReplication)
                            result = task.Result;
                        else
                            OASISResultHolonToHolonHelper<bool, bool>.CopyResult(task.Result, result, false);

                        if (result.IsError || !result.Result)
                        {
                            if (string.IsNullOrEmpty(result.Message) && saveMode != SaveMode.AutoReplication)
                                result.Message = "Unknown.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message), saveMode == SaveMode.AutoReplication);
                        }
                        else
                            result.IsSaved = true;
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."), saveMode == SaveMode.AutoReplication);
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message), saveMode == SaveMode.AutoReplication);
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex, saveMode == SaveMode.AutoReplication);
            }

            return result;
        }

        //TODO: Wish there was a way we could make a generic way to pass a Func in with DIFFERENT params AND return types?! :) Would save a LOT of code above! ;-) lol
        /*
        public async Task<OASISResult<IAvatar>> CallOASISProviderMethodAsync(IAvatar avatar, ProviderType providerType = ProviderType.Default)
        {
            OASISResult<IAvatar> result = new OASISResult<IAvatar>();
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            string errorMessage = $"Error in SaveAvatarDetailForProviderAsync method in AvatarManager saving avatar detail with name {avatar.Name}, username {avatar.Username} and id {avatar.Id} for provider {ProviderManager.CurrentStorageProviderType.Name}. Reason: ";

            try
            {
                OASISResult<IOASISStorageProvider> providerResult = ProviderManager.SetAndActivateCurrentStorageProvider(providerType);

                if (!providerResult.IsError && providerResult.Result != null)
                {
                    var task = providerResult.Result.SaveAvatarAsync(avatar);

                    if (await Task.WhenAny(task, Task.Delay(OASISDNA.OASIS.StorageProviders.ProviderMethodCallTimeOutSeconds)) == task)
                    {
                        result = task.Result;

                        if (result.IsError || result.Result == null)
                        {
                            if (string.IsNullOrEmpty(result.Message))
                                result.Message = "Unknown.";

                            ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, result.Message));
                        }
                        else
                            result.IsSaved = true;
                    }
                    else
                        ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, "timeout occured."));
                }
                else
                    ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, providerResult.Message));
            }
            catch (Exception ex)
            {
                ErrorHandling.HandleWarning(ref result, string.Concat(errorMessage, ex.Message), ex);
            }

            return result;
        }*/
    }
}
