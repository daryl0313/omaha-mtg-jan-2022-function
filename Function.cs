using Google.Cloud.Functions.Framework;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System;
using Google.Cloud.Vision.V1;
using Google.Cloud.TextToSpeech.V1;
using Google.Protobuf;
using System.Text;
using System.Linq;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace DotnetGcf
{
    public class Function : IHttpFunction
    {
        /// <summary>
        /// Logic for your function goes here.
        /// </summary>
        /// <param name="context">The HTTP context, containing the request and the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = int.MaxValue)]
        public async Task HandleAsync(HttpContext context)
        {
            Image image = await GetImage(context.Request, "image");
            if (image == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("\"image\" is required for this request");
                return;
            }

            // var builder = new ImageAnnotatorClientBuilder();
            // builder.CredentialsPath = "key.json";
            // builder.Endpoint = "https://automl.googleapis.com/v1beta1/projects/922566573636/locations/us-central1/models/ICN6818430044629630976:predict";
            // var client = await builder.BuildAsync();
            var client = await ImageAnnotatorClient.CreateAsync();
            var labels = await client.DetectLabelsAsync(image);

            Console.WriteLine("Labels (and confidence score):");
            Console.WriteLine(new String('=', 30));

            var labelResponses = new List<string>();
            foreach (var label in labels)
            {
                string labelResponse = $"{label.Description} ({(int)(label.Score * 100)}%)";
                Console.WriteLine(labelResponse);
                labelResponses.Add(labelResponse);
            }

            string responseText = String.Join("<br />", labelResponses.Select(l => l.ToString()));
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseText);
            context.Response.ContentType = "text/html";
            await context.Response.Body.WriteAsync(responseBytes);



            // HttpRequest request = context.Request;
            // // Check URL parameters for "message" field
            // string message = request.Query["message"];

            // var client = TextToSpeechClient.Create();

            // // The input to be synthesized, can be provided as text or SSML.
            // var input = new SynthesisInput
            // {
            //     Text = message
            // };

            // // Build the voice request.
            // var voiceSelection = new VoiceSelectionParams
            // {
            //     LanguageCode = "en-US",
            //     SsmlGender = SsmlVoiceGender.Female
            // };

            // // Specify the type of audio file.
            // var audioConfig = new AudioConfig
            // {
            //     AudioEncoding = AudioEncoding.Mp3
            // };

            // // Perform the text-to-speech request.
            // var response = client.SynthesizeSpeech(input, voiceSelection, audioConfig);
            // Console.WriteLine("Response Length: {0}", response.AudioContent.Length);

            // context.Response.ContentType = "audio/mpeg";
            // context.Response.Headers.Append("Content-Disposition", "filename=\"response.mp3\"");
            // await context.Response.Body.WriteAsync(response.AudioContent.Memory);
        }

        private async Task<Image> GetImage(HttpRequest request, string imageKey)
        {
            if (request.HasFormContentType && request.Form.Files[imageKey] != null)
            {
                var formImage = request.Form.Files[imageKey];
                var imageBytes = await GetFormFileBytes(formImage);
                return Image.FromBytes(imageBytes);

            }
            else if (request.Query.ContainsKey(imageKey))
            {
                return Image.FromUri(request.Query[imageKey]);
            }
            return null;
        }

        private static async Task<string> GetRequestImageUri(HttpContext context, string fileKey)
        {
            if (context.Request.HasFormContentType && context.Request.Form.Files[fileKey] != null)
            {
                var formImage = context.Request.Form.Files[fileKey];
                var imageBytes = await GetFormFileBytes(formImage);
                return $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";

            }
            else if (context.Request.Query.ContainsKey(fileKey))
            {
                return context.Request.Query[fileKey];
            }

            return null;
        }

        private static async Task<byte[]> GetFormFileBytes(IFormFile imageTest)
        {
            using (var stream = new MemoryStream())
            {
                await imageTest.CopyToAsync(stream);
                return stream.ToArray();
            }
        }
    }
}