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

app.Run();
