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
        PredictionServiceClient _predictionServiceClient { get; set; } = PredictionServiceClient.Create();
        ImageAnnotatorClient _annotatorClient { get; set; } = ImageAnnotatorClient.Create();
        TextToSpeechClient _speechClient { get; set; } = TextToSpeechClient.Create();

        private const string projectId = "omaha-mtg-presentation-339304";
        private const string locationId = "us-central1";
        private const string modelId = "ICN6515615746247098368";

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
                // context.Response.StatusCode = 400;
                // await context.Response.WriteAsync("\"image\" is required for this request");
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(GetOutputHtml(context));
                return;
            }

            PredictRequest request = new PredictRequest
            {
                ModelName = new Google.Cloud.AutoML.V1.ModelName(projectId, locationId, modelId),
                Payload = new ExamplePayload
                {
                    Image = predictionImage
                }
            };
            var predictResponse = await _predictionServiceClient.PredictAsync(request);

            ByteString audioResult;
            if (predictResponse.Payload[0].DisplayName == "hotdog")
            {
                audioResult = await GetAudioMessageByteString("Yes, that looks like a hot dog");
            }
            else
            {

                var image = await GetImage(context.Request, "image");
                var labels = await _annotatorClient.DetectLabelsAsync(image);

                var message = "Could not determine what that is.  Try another image.";
                if (labels.Any())
                {
                    message = $"My best guess is that's a {labels.ElementAt(0).Description}";
                }

                audioResult = await GetAudioMessageByteString(message);
            }


            Console.WriteLine("Response Length: {0}", audioResult.Length);

            // context.Response.ContentType = "audio/mpeg";
            // context.Response.Headers.Append("Content-Disposition", "filename=\"response.mp3\"");
            // await context.Response.Body.WriteAsync(audioResult.Memory);

            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(GetOutputHtml(context, predictionImage.ImageBytes, audioResult));
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

        private async Task<ByteString> GetAudioMessageByteString(string message)
        {
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
            var response = await _speechClient.SynthesizeSpeechAsync(input, voiceSelection, audioConfig);
            return response.AudioContent;
        }

        private string GetOutputHtml(HttpContext context, ByteString image = null, ByteString audio = null)
        {
            return
@$"<html>
  <head><title>Hotdog or Not hotdog</title></head>
  <body>
    <form action="""" method=""POST"" enctype=""multipart/form-data"">
      <p>Upload an image to see if it's a Hotdog or Not hotdog.</p>
      <label>Image:</label>
      <input type=""file"" name=""image"" accept=""image/png, image/jpeg"" required />
      <button type=""submit"">Upload</button>
      </form>
    <section>
      {(image == null ? "" : $"<image alt='hotdog or not' src='{GetImageSource(context, image)}' height='300'/>")}
      <br />
      {(audio == null ? "" : @$"<audio controls='controls' autobuffer='autobuffer' autoplay='autoplay'><source src='data:audio/mp3;base64,{audio.ToBase64()}' /></audio>")}
    </section>
  </body>
</html>";
        }

        private string GetImageSource(HttpContext context, ByteString image)
        {
            if (context.Request.Query.ContainsKey("image"))
            {
                return context.Request.Query["image"];
            }
            return $"data:image/jpeg;base64,{Convert.ToBase64String(image.ToByteArray())}";
        }
    }
}
