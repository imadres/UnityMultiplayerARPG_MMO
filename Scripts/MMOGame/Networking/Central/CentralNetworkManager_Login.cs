﻿using Cysharp.Threading.Tasks;
using LiteNetLib.Utils;
using LiteNetLibManager;
using System.Text.RegularExpressions;

namespace MultiplayerARPG.MMO
{
    public partial class CentralNetworkManager
    {
        public bool RequestUserLogin(string username, string password, ResponseDelegate<ResponseUserLoginMessage> callback)
        {
            return ClientSendRequest(MMORequestTypes.RequestUserLogin, new RequestUserLoginMessage()
            {
                username = username,
                password = password,
            }, responseDelegate: callback);
        }

        public bool RequestUserRegister(string username, string password, string email, ResponseDelegate<ResponseUserRegisterMessage> callback)
        {
            return ClientSendRequest(MMORequestTypes.RequestUserRegister, new RequestUserRegisterMessage()
            {
                username = username,
                password = password,
                email = email,
            }, responseDelegate: callback);
        }

        public bool RequestUserLogout(ResponseDelegate<INetSerializable> callback)
        {
            return ClientSendRequest(MMORequestTypes.RequestUserLogout, EmptyMessage.Value, responseDelegate: callback);
        }

        public bool RequestValidateAccessToken(string userId, string accessToken, ResponseDelegate<ResponseValidateAccessTokenMessage> callback)
        {
            return ClientSendRequest(MMORequestTypes.RequestValidateAccessToken, new RequestValidateAccessTokenMessage()
            {
                userId = userId,
                accessToken = accessToken,
            }, responseDelegate: callback);
        }

        protected async UniTaskVoid HandleRequestUserLogin(
            RequestHandlerData requestHandler,
            RequestUserLoginMessage request,
            RequestProceedResultDelegate<ResponseUserLoginMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            if (disableDefaultLogin)
            {
                result.InvokeError(new ResponseUserLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_SERVICE_NOT_AVAILABLE,
                });
                return;
            }

            long connectionId = requestHandler.ConnectionId;
            AsyncResponseData<ValidateUserLoginResp> validateUserLoginResp = await DbServiceClient.ValidateUserLoginAsync(new ValidateUserLoginReq()
            {
                Username = request.username,
                Password = request.password
            });
            if (!validateUserLoginResp.IsSuccess)
            {
                result.InvokeError(new ResponseUserLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            string userId = validateUserLoginResp.Response.UserId;
            string accessToken = string.Empty;
            long unbanTime = 0;
            if (string.IsNullOrEmpty(userId))
            {
                result.InvokeError(new ResponseUserLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INVALID_USERNAME_OR_PASSWORD,
                });
                return;
            }
            if (userPeersByUserId.ContainsKey(userId) || MapContainsUser(userId))
            {
                result.InvokeError(new ResponseUserLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_ALREADY_LOGGED_IN,
                });
                return;
            }
            bool emailVerified = true;
            if (requireEmailVerification)
            {
                AsyncResponseData<ValidateEmailVerificationResp> validateEmailVerificationResp = await DbServiceClient.ValidateEmailVerificationAsync(new ValidateEmailVerificationReq()
                {
                    UserId = userId
                });
                if (!validateEmailVerificationResp.IsSuccess)
                {
                    result.InvokeError(new ResponseUserLoginMessage()
                    {
                        message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                    });
                    return;
                }
                emailVerified = validateEmailVerificationResp.Response.IsPass;
            }
            AsyncResponseData<GetUserUnbanTimeResp> unbanTimeResp = await DbServiceClient.GetUserUnbanTimeAsync(new GetUserUnbanTimeReq()
            {
                UserId = userId
            });
            if (!unbanTimeResp.IsSuccess)
            {
                result.InvokeError(new ResponseUserLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            unbanTime = unbanTimeResp.Response.UnbanTime;
            if (unbanTime > System.DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                result.InvokeError(new ResponseUserLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_USER_BANNED,
                });
                return;
            }
            if (!emailVerified)
            {
                result.InvokeError(new ResponseUserLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_EMAIL_NOT_VERIFIED,
                });
                return;
            }
            CentralUserPeerInfo userPeerInfo = new CentralUserPeerInfo();
            userPeerInfo.connectionId = connectionId;
            userPeerInfo.userId = userId;
            userPeerInfo.accessToken = accessToken = Regex.Replace(System.Convert.ToBase64String(System.Guid.NewGuid().ToByteArray()), "[/+=]", "");
            userPeersByUserId[userId] = userPeerInfo;
            userPeers[connectionId] = userPeerInfo;
            AsyncResponseData<EmptyMessage> updateAccessTokenResp = await DbServiceClient.UpdateAccessTokenAsync(new UpdateAccessTokenReq()
            {
                UserId = userId,
                AccessToken = accessToken
            });
            if (!updateAccessTokenResp.IsSuccess)
            {
                result.InvokeError(new ResponseUserLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            // Response
            result.InvokeSuccess(new ResponseUserLoginMessage()
            {
                userId = userId,
                accessToken = accessToken,
                unbanTime = unbanTime,
            });
#endif
        }

        protected async UniTaskVoid HandleRequestUserRegister(
            RequestHandlerData requestHandler,
            RequestUserRegisterMessage request,
            RequestProceedResultDelegate<ResponseUserRegisterMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            if (disableDefaultLogin)
            {
                result.InvokeError(new ResponseUserRegisterMessage()
                {
                    message = UITextKeys.UI_ERROR_SERVICE_NOT_AVAILABLE
                });
                return;
            }
            string username = request.username.Trim();
            string password = request.password.Trim();
            string email = request.email.Trim();
            if (!NameValidating.ValidateUsername(username))
            {
                result.InvokeError(new ResponseUserRegisterMessage()
                {
                    message = UITextKeys.UI_ERROR_INVALID_USERNAME
                });
                return;
            }
            if (requireEmail)
            {
                if (string.IsNullOrEmpty(email) || !Email.IsValid(email))
                {
                    result.InvokeError(new ResponseUserRegisterMessage()
                    {
                        message = UITextKeys.UI_ERROR_INVALID_EMAIL,
                    });
                    return;
                }
                AsyncResponseData<FindEmailResp> findEmailResp = await DbServiceClient.FindEmailAsync(new FindEmailReq()
                {
                    Email = email
                });
                if (!findEmailResp.IsSuccess)
                {
                    result.InvokeError(new ResponseUserRegisterMessage()
                    {
                        message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                    });
                    return;
                }
                if (findEmailResp.Response.FoundAmount > 0)
                {
                    result.InvokeError(new ResponseUserRegisterMessage()
                    {
                        message = UITextKeys.UI_ERROR_EMAIL_ALREADY_IN_USE,
                    });
                    return;
                }
            }
            AsyncResponseData<FindUsernameResp> findUsernameResp = await DbServiceClient.FindUsernameAsync(new FindUsernameReq()
            {
                Username = username
            });
            if (!findUsernameResp.IsSuccess)
            {
                result.InvokeError(new ResponseUserRegisterMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            if (findUsernameResp.Response.FoundAmount > 0)
            {
                result.InvokeError(new ResponseUserRegisterMessage()
                {
                    message = UITextKeys.UI_ERROR_USERNAME_EXISTED,
                });
                return;
            }
            if (string.IsNullOrEmpty(username) || username.Length < minUsernameLength)
            {
                result.InvokeError(new ResponseUserRegisterMessage()
                {
                    message = UITextKeys.UI_ERROR_USERNAME_TOO_SHORT,
                });
                return;
            }
            if (username.Length > maxUsernameLength)
            {
                result.InvokeError(new ResponseUserRegisterMessage()
                {
                    message = UITextKeys.UI_ERROR_USERNAME_TOO_LONG,
                });
                return;
            }
            if (string.IsNullOrEmpty(password) || password.Length < minPasswordLength)
            {
                result.InvokeError(new ResponseUserRegisterMessage()
                {
                    message = UITextKeys.UI_ERROR_PASSWORD_TOO_SHORT,
                });
                return;
            }
            AsyncResponseData<EmptyMessage> createResp = await DbServiceClient.CreateUserLoginAsync(new CreateUserLoginReq()
            {
                Username = username,
                Password = password,
                Email = email,
            });
            if (!createResp.IsSuccess)
            {
                result.InvokeError(new ResponseUserRegisterMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            // Response
            result.InvokeSuccess(new ResponseUserRegisterMessage());
#endif
        }

        protected async UniTaskVoid HandleRequestUserLogout(
            RequestHandlerData requestHandler,
            EmptyMessage request,
            RequestProceedResultDelegate<EmptyMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            long connectionId = requestHandler.ConnectionId;
            CentralUserPeerInfo userPeerInfo;
            if (userPeers.TryGetValue(connectionId, out userPeerInfo))
            {
                userPeersByUserId.Remove(userPeerInfo.userId);
                userPeers.Remove(connectionId);
                AsyncResponseData<EmptyMessage> updateAccessTokenResp = await DbServiceClient.UpdateAccessTokenAsync(new UpdateAccessTokenReq()
                {
                    UserId = userPeerInfo.userId,
                    AccessToken = string.Empty
                });
                if (!updateAccessTokenResp.IsSuccess)
                {
                    result.InvokeError(EmptyMessage.Value);
                    return;
                }
            }
            // Response
            result.InvokeSuccess(EmptyMessage.Value);
#endif
        }

        protected async UniTaskVoid HandleRequestValidateAccessToken(
            RequestHandlerData requestHandler,
            RequestValidateAccessTokenMessage request,
            RequestProceedResultDelegate<ResponseValidateAccessTokenMessage> result)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
            long connectionId = requestHandler.ConnectionId;
            string userId = request.userId;
            string accessToken = request.accessToken;
            long unbanTime = 0;
            AsyncResponseData<ValidateAccessTokenResp> validateAccessTokenResp = await DbServiceClient.ValidateAccessTokenAsync(new ValidateAccessTokenReq()
            {
                UserId = userId,
                AccessToken = accessToken
            });
            if (!validateAccessTokenResp.IsSuccess)
            {
                result.InvokeError(new ResponseValidateAccessTokenMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            if (!validateAccessTokenResp.Response.IsPass)
            {
                result.InvokeError(new ResponseValidateAccessTokenMessage()
                {
                    message = UITextKeys.UI_ERROR_INVALID_USER_TOKEN,
                });
                return;
            }
            AsyncResponseData<GetUserUnbanTimeResp> unbanTimeResp = await DbServiceClient.GetUserUnbanTimeAsync(new GetUserUnbanTimeReq()
            {
                UserId = userId
            });
            if (!unbanTimeResp.IsSuccess)
            {
                result.InvokeError(new ResponseValidateAccessTokenMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            unbanTime = unbanTimeResp.Response.UnbanTime;
            if (unbanTime > System.DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                result.InvokeError(new ResponseValidateAccessTokenMessage()
                {
                    message = UITextKeys.UI_ERROR_USER_BANNED,
                });
                return;
            }
            CentralUserPeerInfo userPeerInfo;
            if (userPeersByUserId.TryGetValue(userId, out userPeerInfo))
            {
                userPeersByUserId.Remove(userPeerInfo.userId);
                userPeers.Remove(userPeerInfo.connectionId);
            }
            userPeerInfo = new CentralUserPeerInfo();
            userPeerInfo.connectionId = connectionId;
            userPeerInfo.userId = userId;
            userPeerInfo.accessToken = accessToken = Regex.Replace(System.Convert.ToBase64String(System.Guid.NewGuid().ToByteArray()), "[/+=]", "");
            userPeersByUserId[userId] = userPeerInfo;
            userPeers[connectionId] = userPeerInfo;
            AsyncResponseData<EmptyMessage> updateAccessTokenResp = await DbServiceClient.UpdateAccessTokenAsync(new UpdateAccessTokenReq()
            {
                UserId = userPeerInfo.userId,
                AccessToken = accessToken
            });
            if (!updateAccessTokenResp.IsSuccess)
            {
                result.InvokeError(new ResponseValidateAccessTokenMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                });
                return;
            }
            // Response
            result.InvokeSuccess(new ResponseValidateAccessTokenMessage()
            {
                userId = userId,
                accessToken = accessToken,
            });
#endif
        }
    }
}
