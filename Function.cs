using Google.Cloud.Functions.Framework;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System;
using Google.Cloud.Vision.V1;
using Google.Cloud.TextToSpeech.V1;
using Google.Protobuf;
using System.Linq;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Google.Cloud.AutoML.V1;
using System.Net.Http;

namespace DotnetGcf
{
    public class Function : IHttpFunction
    {
        private const string projectId = "omaha-mtg-presentation";
        private const string locationId = "us-central1";
        private const string modelId = "ICN6818430044629630976";

        /// <summary>
        /// Logic for your function goes here.
        /// </summary>
        /// <param name="context">The HTTP context, containing the request and the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = int.MaxValue)]
        public async Task HandleAsync(HttpContext context)
        {
            var predictionImage = await GetPredictionImage(context.Request, "image");
            if (predictionImage == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("\"image\" is required for this request");
                return;
            }
            var client = await PredictionServiceClient.CreateAsync();
            PredictRequest request = new PredictRequest
            {
                ModelName = new Google.Cloud.AutoML.V1.ModelName(projectId, locationId, modelId),
                Payload = new ExamplePayload
                {
                    Image = predictionImage
                }
            };
            var predictResponse = await client.PredictAsync(request);

            ByteString audioResult;
            if (predictResponse.Payload[0].DisplayName == "hotdog")
            {
                audioResult = await GetAudioMessageByteString("Yes, that looks like a hot dog");
            }
            else
            {
                var annotatorClient = await ImageAnnotatorClient.CreateAsync();

                var image = await GetImage(context.Request, "image");
                var labels = await annotatorClient.DetectLabelsAsync(image);

                var message = "Could not determine what that is.  Try another image.";
                if (labels.Any())
                {
                    message = $"My best guess is that's a {labels.ElementAt(0).Description}";
                }

                audioResult = await GetAudioMessageByteString(message);
            }


            Console.WriteLine("Response Length: {0}", audioResult.Length);

            context.Response.ContentType = "audio/mpeg";
            context.Response.Headers.Append("Content-Disposition", "filename=\"response.mp3\"");
            await context.Response.Body.WriteAsync(audioResult.Memory);
        }

        private async Task<Google.Cloud.Vision.V1.Image> GetImage(HttpRequest request, string imageKey)
        {
            if (request.HasFormContentType && request.Form.Files[imageKey] != null)
            {
                var formImage = request.Form.Files[imageKey];
                var imageBytes = await GetFormFileBytes(formImage);
                return Google.Cloud.Vision.V1.Image.FromBytes(imageBytes);

            }
            else if (request.Query.ContainsKey(imageKey))
            {
                return Google.Cloud.Vision.V1.Image.FromUri(request.Query[imageKey]);
            }
            return null;
        }

        private async Task<Google.Cloud.AutoML.V1.Image> GetPredictionImage(HttpRequest request, string imageKey)
        {
            var imageBytes = await GetImageBytes(request, "image");
            if (imageBytes == null)
            {
                return null;
            }
            return new Google.Cloud.AutoML.V1.Image
            {
                ImageBytes = imageBytes
            };
        }

        private async Task<ByteString> GetImageBytes(HttpRequest request, string imageKey)
        {
            if (request.HasFormContentType && request.Form.Files[imageKey] != null)
            {
                var formImage = request.Form.Files[imageKey];
                var imageBytes = await GetFormFileBytes(formImage);
                return ByteString.CopyFrom(imageBytes);

            }
            else if (request.Query.ContainsKey(imageKey))
            {
                string imageUri = request.Query[imageKey];
                return await GetByteStringFromFileUri(imageUri);
            }
            return null;
        }

        private static async Task<ByteString> GetByteStringFromFileUri(string imageUri)
        {
            using HttpClient c = new HttpClient();
            using Stream s = await c.GetStreamAsync(imageUri);
            return await ByteString.FromStreamAsync(s);
        }

        private static async Task<byte[]> GetFormFileBytes(IFormFile imageTest)
        {
            using (var stream = new MemoryStream())
            {
                await imageTest.CopyToAsync(stream);
                return stream.ToArray();
            }
        }

        private static async Task<ByteString> GetAudioMessageByteString(string message)
        {
            var speechClient = TextToSpeechClient.Create();

            // The input to be synthesized, can be provided as text or SSML.
            var input = new SynthesisInput
            {
                Text = message
            };

            // Build the voice request.
            var voiceSelection = new VoiceSelectionParams
            {
                LanguageCode = "en-US",
                SsmlGender = SsmlVoiceGender.Female
            };

            // Specify the type of audio file.
            var audioConfig = new AudioConfig
            {
                AudioEncoding = AudioEncoding.Mp3
            };

            // Perform the text-to-speech request.
            var response = await speechClient.SynthesizeSpeechAsync(input, voiceSelection, audioConfig);
            return response.AudioContent;
        }
    }
}