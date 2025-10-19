using QRCoder;
using Microsoft.AspNetCore.Mvc;

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

app.Run();
