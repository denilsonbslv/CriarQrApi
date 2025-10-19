using QRCoder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/gerar-qrcode", ([FromQuery] string text) =>
{
    if (string.IsNullOrEmpty(text))
    {
        return Results.BadRequest("O parâmetro 'text' é obrigatório.");
    }

    try
    {
        using(QRCodeGenerator qrGenerator = new QRCodeGenerator())
        {
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q))
            {
                using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
                {
                    byte[] qrCodeImage = qrCode.GetGraphic(20);

                    return Results.File(qrCodeImage, "image/png");
                }
            }
        }
    }
    catch (Exception ex)
    {
        return Results.Problem($"Erro ao gerar QR Code: {ex.Message}", statusCode: 500);
    }
})
    .WithName("GerarQrCode")
    .WithDescription("Gera um QR Code em PNG a partir de um texto.");

app.MapGet("/gerar-qrcode-base64", ([FromQuery] string text) =>
{
    if (string.IsNullOrEmpty(text))
    {
        return Results.BadRequest("O parâmetro 'text' é obrigatório.");
    }

    try
    {
        using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
        {
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q))
            {
                using (Base64QRCode qrCode = new Base64QRCode(qrCodeData))
                {
                    string qrCodeBase64Pura = qrCode.GetGraphic(20);

                    string qrCodeBase64ComPrefixo = $"data:image/png;base64,{qrCodeBase64Pura}";

                    return Results.Ok(new
                    {
                        originalText = text,
                        imagemBase64 = qrCodeBase64ComPrefixo
                    });
                }
            }
        }
    }
    catch (Exception ex)
    {
        return Results.Problem($"Erro ao gerar QR Code: {ex.Message}", statusCode: 500);
    }
})
    .WithName("GerarQrCodeBase64")
    .WithDescription("Gera um QR Code a partir de um texto e o retorna como JSON (string Base64).");


app.MapPost("/gerar-qrcode-dinamico",
   async ([FromForm] QrCodeRequest request) =>
    {
        if (string.IsNullOrEmpty(request.Text))
        {
            return Results.BadRequest("O parâmetro 'texto' é obrigatório.");
        }

        try
        {
            var eccLevel = (request.Logo == null || request.Logo.Length == 0)
                ? QRCodeGenerator.ECCLevel.Q
                : QRCodeGenerator.ECCLevel.H;

            byte[] qrCodeImageBytes;
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            {
                using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(request.Text, eccLevel))
                {
                    using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
                    {
                        qrCodeImageBytes = qrCode.GetGraphic(20);
                    }
                }
            }

            if (request.Logo == null || request.Logo.Length == 0)
            {
                return Results.File(qrCodeImageBytes, "image/png");
            }

            using (Image qrTemp = Image.Load(qrCodeImageBytes))
            {
                using (var canvas = new Image<Rgba32>(qrTemp.Width, qrTemp.Height, Color.White))
                {
                    using (Image qrDrawable = Image.Load(qrCodeImageBytes))
                    {
                        canvas.Mutate(x => x.DrawImage(qrDrawable, new Point(0, 0), 1f));
                    }

                    using (Image logoImage = await Image.LoadAsync(request.Logo.OpenReadStream()))
                    {
                        int logoSize = canvas.Width / 5;

                        logoImage.Mutate(x => x.Resize(logoSize, logoSize));

                        int logoXPos = (canvas.Width - logoImage.Width) / 2;
                        int logoYPos = (canvas.Height - logoImage.Height) / 2;

                        int padding = 10; // 10 pixels de margem

                        RectangleF backgroundArea = new RectangleF(
                            logoXPos - padding,
                            logoYPos - padding,
                            logoImage.Width + (padding * 2),
                            logoImage.Height + (padding * 2)
                        );

                        canvas.Mutate(x => x.Fill(Color.White, backgroundArea));

                        canvas.Mutate(x => x.DrawImage(logoImage, new Point(logoXPos, logoYPos), 1f));
                    }

                    using (var ms = new MemoryStream())
                    {
                        await canvas.SaveAsPngAsync(ms);
                        return Results.File(ms.ToArray(), "image/png");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("Image cannot be loaded"))
            {
                return Results.BadRequest("Erro: O arquivo enviado não é uma imagem válida (PNG, JPG, etc.).");
            }
            return Results.Problem($"Erro ao gerar QR Code: {ex.Message}", statusCode: 500);
        }
    })
    .Accepts<QrCodeRequest>("multipart/form-data")
    .DisableAntiforgery()
    .WithName("GerarQrCodeDinamicoPost")
    .WithDescription("Gera um QR Code. Aceita texto e um logo opcional (via multipart/form-data) para centralizar.");

app.Run();


public class QrCodeRequest
{
    public string Text { get; set; }

    public IFormFile? Logo { get; set; }
}