﻿//
//  API.AI .NET SDK - client-side libraries for API.AI
//  =================================================
//
//  Copyright (C) 2015 by Speaktoit, Inc. (https://www.speaktoit.com)
//  https://www.api.ai
//
//  ***********************************************************************************************************************
//
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with
//  the License. You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on
//  an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the
//  specific language governing permissions and limitations under the License.
//
//  ***********************************************************************************************************************

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using ApiAiSDK.Model;
using Newtonsoft.Json;

namespace ApiAiSDK
{
    public class AIDataService
    {
        private const string SESSIONID_SETTING = "api_ai_SessionId";

        private readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly AIConfiguration config;

        private string sessionId;

        /// <summary>
        /// Unique SessionId. Normally should not be changed.
        /// </summary>
        public string SessionId
        {
            get
            {
                return sessionId;
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentNullException();
                }

                if (value.Length < 30)
                {
                    throw new ArgumentException("SessionId must be longer than 30 symbols. It is recommended to use Guid.NewGuid().ToString()");
                }
                sessionId = value;
            }
        }

        private readonly HttpClient httpClient;

        public AIDataService(AIConfiguration config)
        {
            this.config = config;

            if (string.IsNullOrEmpty(config.SessionId))
            {
                sessionId = Guid.NewGuid().ToString();
            }
            else
            {
                sessionId = config.SessionId;
            }
            

            httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.Authorization = new HttpCredentialsHeaderValue("Bearer", config.ClientAccessToken);
            httpClient.DefaultRequestHeaders.Accept.Add(new HttpMediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<AIResponse> RequestAsync(AIRequest request)
        {
            return await RequestAsync(request, CancellationToken.None);
        }

        public async Task<AIResponse> RequestAsync(AIRequest request, CancellationToken cancellationToken)
        {
            request.Language = config.Language.code;
            request.Timezone = TimeZoneInfo.Local.StandardName;
            request.SessionId = sessionId;

            try
            {
                var jsonRequest = JsonConvert.SerializeObject(request, Formatting.None, jsonSettings);

                if (config.DebugLog)
                {
                    Debug.WriteLine($"Request: {jsonRequest}");
                }

                var content = new HttpStringContent(jsonRequest, UnicodeEncoding.Utf8, "application/json");

                var response = await httpClient.PostAsync(new Uri(config.RequestUrl), content).AsTask(cancellationToken);
                return await ProcessResponse(response);

            }
            catch (AIServiceException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new AIServiceException(e);
            }
        }
        
        public async Task<AIResponse> VoiceRequestAsync(Stream voiceStream, RequestExtras requestExtras = null)
        {
            var request = new AIRequest
            {
                Language = config.Language.code,
                Timezone = TimeZoneInfo.Local.StandardName,
                SessionId = sessionId
            };

            if (requestExtras != null)
            {
                if (requestExtras.HasContexts)
                {
                    request.Contexts = requestExtras.Contexts;
                }

                if (requestExtras.HasEntities)
                {
                    request.Entities = requestExtras.Entities;
                }
            }

            try
            {
                var content = new HttpMultipartFormDataContent();
                
                var jsonRequest = JsonConvert.SerializeObject(request, Formatting.None, jsonSettings);

                if (config.DebugLog)
                {
                    Debug.WriteLine($"Request: {jsonRequest}");
                }

                content.Add(new HttpStringContent(jsonRequest, UnicodeEncoding.Utf8, "application/json"), "request");
                content.Add(new HttpStreamContent(voiceStream.AsInputStream()), "voiceData", "voice.wav");
                
                var response = await httpClient.PostAsync(new Uri(config.RequestUrl), content);
                return await ProcessResponse(response);
            }
            catch (Exception e)
            {
                throw new AIServiceException(e);
            }
        }

        public async Task<bool> ResetContextsAsync()
        {
            var cleanRequest = new AIRequest("empty_query_for_resetting_contexts");
            cleanRequest.ResetContexts = true;
            try
            {
                var response = await RequestAsync(cleanRequest);
                return !response.IsError;
            }
            catch (AIServiceException e)
            {
                Debug.WriteLine("Exception while contexts clean." + e);
                return false;
            }
        }

        /// <summary>
        /// Persist sessionId to LocalSettings. Later sessionId could be restored using RestoreSessionId()
        /// </summary>
        public void PersistSessionId()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values[SESSIONID_SETTING] = sessionId;
        }

        /// <summary>
        /// Restore sessionId from LocalSettings
        /// </summary>
        public void RestoreSessionId()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.ContainsKey(SESSIONID_SETTING))
            {
                var restoredSessionId = Convert.ToString(localSettings.Values[SESSIONID_SETTING]);
                if (!string.IsNullOrEmpty(restoredSessionId) && restoredSessionId.Length > 30)
                {
                    SessionId = restoredSessionId;
                }
            }
        }

        private Task<AIResponse> ProcessResponse(HttpResponseMessage response)
        {
            return ProcessResponse(response, CancellationToken.None);
        }

        private async Task<AIResponse> ProcessResponse(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
            {
                var stringResult = await response.Content.ReadAsStringAsync().AsTask(cancellationToken);
                if (config.DebugLog)
                {
                    Debug.WriteLine($"Response: {stringResult}");
                }

                var aiResponse = JsonConvert.DeserializeObject<AIResponse>(stringResult);
                CheckForErrors(aiResponse);

                return aiResponse;
            }
            else
            {
                throw new AIServiceException(
                    $"Request to the API.AI service failed with code {response.StatusCode} and message '{response.ReasonPhrase}'");
            }
        }

        static void CheckForErrors(AIResponse aiResponse)
        {
            if (aiResponse == null)
            {
                throw new AIServiceException("API.AI response parsed as null. Check debug log for details.");
            }

            if (aiResponse.IsError)
            {
                throw new AIServiceException(aiResponse);
            }
        }
    }
}
