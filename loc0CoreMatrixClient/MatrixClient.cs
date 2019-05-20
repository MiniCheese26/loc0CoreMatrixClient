﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using loc0CoreMatrixClient.Events;
using loc0CoreMatrixClient.Exceptions;
using loc0CoreMatrixClient.Models;
using MimeTypes;
using Newtonsoft.Json.Linq;

namespace loc0CoreMatrixClient
{
    /// <summary>
    /// Client for interacting with the Matrix API
    /// </summary>
    public class MatrixClient
    {
        private readonly int _messageLimit;
        private MatrixHttp _backendHttpClient;
        private MatrixListener _matrixListener;
        private CancellationTokenSource _syncCancellationToken;
        internal Dictionary<string, MatrixRoom> Rooms = new Dictionary<string, MatrixRoom>();

        /// <summary>
        /// Id of filter used for API sync calls
        /// </summary>
        public string FilterId { get; private set; }

        /// <summary>
        /// AccessToken to be used when interacting with the API
        /// </summary>
        public string AccessToken { get; private set; }

        /// <summary>
        /// DeviceId in use
        /// </summary>
        public string DeviceId { get; private set; }

        /// <summary>
        /// Homeserver the client is connected to
        /// </summary>
        public string HomeServer { get; private set; }

        /// <summary>
        /// Full userid of account
        /// </summary>
        public string UserId { get; private set; }

        /// <param name="messageLimit">Number of messages to take on each sync</param>
        public MatrixClient(int messageLimit = 10)
        {
            _messageLimit = messageLimit;
        }

        /// <inheritdoc />
        public delegate void MessageReceivedEvent(MessageReceivedEventArgs args);

        /// <inheritdoc />
        public delegate void InviteReceivedEvent(InviteReceivedEventArgs args);

        /// <summary>
        /// Event for any incoming messages
        /// </summary>
        public event MessageReceivedEvent MessageReceived;

        /// <summary>
        /// Event for any incoming invites
        /// </summary>
        public event InviteReceivedEvent InviteReceived;

        /// <summary>
        /// Login to a Matrix account
        /// </summary>
        /// <param name="host">Homeserver the account uses</param>
        /// <param name="credentials">MatrixCredentials object that contains login info</param>
        /// <returns>Bool based on success or failure</returns>
        public async Task<bool> Login(string host, MatrixCredentials credentials)
        {
            _backendHttpClient = new MatrixHttp(host);

            JObject loginJObject = JObject.FromObject(credentials);

            HttpResponseMessage loginResponse = await _backendHttpClient.Post("/_matrix/client/r0/login", false, loginJObject);

            string loginResponseContent = string.Empty;

            try
            {
                loginResponseContent = await loginResponse.Content.ReadAsStringAsync();
                loginResponse.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException)
            {
                JObject error = JObject.Parse(loginResponseContent);

                switch (loginResponse.StatusCode)
                {
                    case HttpStatusCode.BadRequest:
                        throw new MatrixRequestException($"{error["errcode"]} - {error["error"]}. Bad login request");
                    case HttpStatusCode.Forbidden:
                        throw new MatrixRequestException($"{error["error"]}. Login credentials were incorrect");
                    case HttpStatusCode.TooManyRequests:
                        throw new MatrixRequestException($"{error["errcode"]} - {error["error"]}. Too many requests, you're being rate limited"); //Need to implement a rate limiting
                    default:
                        throw new MatrixRequestException($"{error["errcode"] ?? ""} - {error["error"] ?? ""}. Unknown error occured, error code {loginResponse.StatusCode.ToString()}");
                }
            }

            var loginResponseJObject = JObject.Parse(loginResponseContent);

            AccessToken = (string)loginResponseJObject["access_token"];
            DeviceId = (string)loginResponseJObject["device_id"];
            HomeServer = (string)loginResponseJObject["home_server"];
            UserId = (string)loginResponseJObject["user_id"];

            _backendHttpClient.SetAccessToken(AccessToken);

            var filtersResult = await UploadFilters();

            if (!filtersResult)
            {
                Console.WriteLine("Failed to upload filters");
            }

            return true;
        }

        private async Task<bool> UploadFilters()
        {
            var filterJObject = new JObject
            {
                ["room"] = new JObject
                {
                    ["state"] = new JObject
                    {
                        ["not_types"] = new JArray("*")
                    },

                    ["timeline"] = new JObject
                    {
                        ["limit"] = _messageLimit,
                        ["types"] = new JArray("m.room.message")
                    },

                    ["ephemeral"] = new JObject
                    {
                        ["not_types"] = new JArray("*")
                    }
                },

                ["presence"] = new JObject
                {
                    ["not_types"] = new JArray("*")
                },

                ["event_format"] = "client",

                ["event_fields"] = new JArray("content", "sender")
            };

            var filterUrl = $"/_matrix/client/r0/user/{HttpUtility.UrlEncode(UserId)}/filter";

            HttpResponseMessage filterResponse =
                await _backendHttpClient.Post(filterUrl, true, filterJObject);

            try
            {
                filterResponse.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException)
            {
                return false;
            }

            var filterResponseContent = await filterResponse.Content.ReadAsStringAsync();
            JObject filterJObjectParsed = JObject.Parse(filterResponseContent);
            FilterId = (string)filterJObjectParsed["filter_id"];

            return true;
        }

        /// <summary>
        /// Logout of your current account, resets all properties
        /// </summary>
        /// <returns>Bool based on success or failure</returns>
        public async Task<bool> Logout()
        {
            HttpResponseMessage logoutResponse = await _backendHttpClient.Post("/_matrix/client/r0/logout", true, null);

            try
            {
                logoutResponse.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException)
            {
                return false;
            }

            _syncCancellationToken.Cancel();

            AccessToken = "";
            DeviceId = "";
            UserId = "";
            HomeServer = "";

            return true;
        }

        /// <summary>
        /// Join multiple rooms via a list
        /// </summary>
        /// <param name="roomsToJoin">List of rooms to join, can be alias or id</param>
        /// <param name="retryFailure">If connecting to a room fails, it will retry until success</param>
        /// <returns>List of strings denoting failure or success</returns>
        public async Task<List<string>> JoinRooms(List<string> roomsToJoin, bool retryFailure = false) //replace List<string> with something else, i don't know what yet
        {
            var responseList = new List<string>();
            int roomSuccessfullyJoined = 1;

            for (var i = 0; i < roomsToJoin.Count; i++)
            {
                var room = roomsToJoin[i];
                Console.Write($"\rJoining room [{roomSuccessfullyJoined}/{roomsToJoin.Count}]");

                if (Rooms.ContainsKey(room))
                {
                    Console.WriteLine($"Already joined {room}");
                    continue;
                }

                HttpResponseMessage roomResponse = await _backendHttpClient.Post($"/_matrix/client/r0/join/{HttpUtility.UrlEncode(room)}", true, null);

                try
                {
                    roomResponse.EnsureSuccessStatusCode();

                    responseList.Add($"Successfully Joined {room}");
                    JObject roomResponseJObject = JObject.Parse(await roomResponse.Content.ReadAsStringAsync());

                    MatrixRoom newRoom = new MatrixRoom(HomeServer, AccessToken, (string)roomResponseJObject["room_id"], room);
                    Rooms.Add(newRoom.RoomId, newRoom);
                    roomSuccessfullyJoined++;
                }
                catch (HttpRequestException)
                {
                    if (retryFailure)
                    {
                        i = i == 0 ? 0 : i - 1;
                    }
                    else
                    {
                        responseList.Add($"Failed to Join {room}");
                    }
                }

                await Task.Delay(2000);
            }

            Console.WriteLine(); //since the progress output is a Write not Writeline this is needed so the next thing outputted will be on the next line

            return responseList;
        }

        /// <summary>
        /// Join a single room
        /// </summary>
        /// <param name="roomToJoin">Room to join, can be alias or id</param>
        /// <param name="retryFailure">If connecting to a room fails, it will retry until success</param>
        /// <returns>String denoting failure or success</returns>
        public async Task<string> JoinRoom(string roomToJoin, bool retryFailure = false)
        {
            var response = await JoinRooms(new List<string>
            {
                roomToJoin
            }, retryFailure);

            return response[0];
        }

        /// <summary>
        /// Used to access a dictionary containing MatrixRoom objects for each room you've joined
        /// </summary>
        /// <param name="roomId">Room ID which acts as the key</param>
        /// <returns>MatrixRoom object for that room</returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public MatrixRoom GetMatrixRoomObject(string roomId)
        {
            if (Rooms.TryGetValue(roomId, out MatrixRoom room))
            {
                return room;
            }

            throw new KeyNotFoundException("roomId not found");
        }

        /// <summary>
        /// Upload a file to Matrix
        /// </summary>
        /// <param name="filePath">Path to the file you want to upload</param>
        /// <param name="contentType">Optionally specify content type, otherwise it will be automatically detected</param>
        /// <returns>MatrixFileMessage with MxcUrl and Type</returns>
        /// <exception cref="FileNotFoundException"></exception>
        public async Task<MatrixFileMessage> Upload(string filePath, string contentType = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", Path.GetFileName(filePath));

            if (contentType == null) contentType = MimeTypeMap.GetMimeType(Path.GetExtension(filePath)); //credit to samuelneff for mime types https://github.com/samuelneff/MimeTypeMap
            var filename = Path.GetFileName(filePath);
            var fileBytes = File.ReadAllBytes(filePath);

            HttpResponseMessage uploadResponse =
                await _backendHttpClient.Post($"/_matrix/media/r0/upload?filename={HttpUtility.UrlEncode(filename)}", true, fileBytes,
                    new Dictionary<string, string>() { { "Content-Type", contentType } });

            try
            {
                uploadResponse.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException)
            {
                return null;
            }

            var uploadResponseContent = await uploadResponse.Content.ReadAsStringAsync();

            JObject uploadResponseJObject = JObject.Parse(uploadResponseContent);

            MatrixFileMessage matrixFileMessage = new MatrixFileMessage
            {
                MxcUrl = (string)uploadResponseJObject["content_uri"],
                Filename = filename,
                Description = filename
            };

            var contentTypeSplit = contentType.Split("/")[0];

            switch (contentTypeSplit)
            {
                case "image":
                case "video":
                case "audio":
                    matrixFileMessage.Type = $"m.{contentTypeSplit}";
                    break;
                default:
                    matrixFileMessage.Type = "m.file";
                    break;
            }

            return matrixFileMessage;
        }

        /// <summary>
        /// Starts an event listener for any rooms you've joined
        /// </summary>
        public async void StartListener()
        {
            if (_syncCancellationToken == null || _syncCancellationToken.IsCancellationRequested)
            {
                _syncCancellationToken = new CancellationTokenSource();
            }

            _matrixListener = new MatrixListener();

            await Task.Run(() => _matrixListener.Sync(this, _syncCancellationToken.Token), _syncCancellationToken.Token);
        }

        /// <summary>
        /// Stop listening for events in the rooms you've joined
        /// </summary>
        public void StopListener() => _syncCancellationToken?.Cancel();

        internal void OnMessageReceived(MessageReceivedEventArgs args) => MessageReceived?.Invoke(args);

        internal void OnInviteReceived(InviteReceivedEventArgs args) => InviteReceived?.Invoke(args);
    }
}
