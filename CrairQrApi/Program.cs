using QRCoder;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Shapes;

// Aplicação mínima (Minimal API) que expõe endpoints para gerar QR Codes.
// Observações:
// - Mantive toda a lógica original e apenas adicionei comentários explicativos.
// - Endpoints aceitam texto via query string ou via multipart/form-data (para logo).

var builder = WebApplication.CreateBuilder(args);

// Registra serviços para documentação OpenAPI/Swagger (apenas para ambiente de desenvolvimento).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Ativa o Swagger UI quando estiver em ambiente de desenvolvimento.
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// -------------------------------------------------------------------------
// Endpoint: GET /gerar-qrcode
// - Parâmetro: text (query string)
// - Retorno: imagem PNG com o QR Code
// - Erros: 400 se 'text' não informado, 500 em caso de falha interna
// -------------------------------------------------------------------------
app.MapGet("/gerar-qrcode", ([FromQuery] string text) =>
{
    if (string.IsNullOrEmpty(text))
    {
        return Results.BadRequest("O parâmetro 'text' é obrigatório.");
    }

    try
    {
        // Gera o QR Code em memória usando QRCoder e o retorna como PNG bruto.
        using(QRCodeGenerator qrGenerator = new QRCodeGenerator())
        {
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q))
            {
                using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
                {
                    // GetGraphic(20) gera o bitmap do QR Code com uma escala definida.
                    byte[] qrCodeImage = qrCode.GetGraphic(20);

                    // Retorna o conteúdo binário como 'image/png'.
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

// -------------------------------------------------------------------------
// Endpoint: GET /gerar-qrcode-base64
// - Parâmetro: text (query string)
// - Retorno: JSON contendo o texto original e a imagem em Base64 (com prefixo data URI)
// - Útil para quando o cliente prefere receber a imagem como string Base64
// -------------------------------------------------------------------------
app.MapGet("/gerar-qrcode-base64", ([FromQuery] string text) =>
{
    if (string.IsNullOrEmpty(text))
    {
        return Results.BadRequest("O parâmetro 'text' é obrigatório.");
    }

    try
    {
        // Gera o QR Code e converte para Base64 usando o gerador de Base64 do QRCoder.
        using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
        {
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q))
            {
                using (Base64QRCode qrCode = new Base64QRCode(qrCodeData))
                {
                    // Retorna apenas a string Base64 (sem o prefixo) e em seguida adicionamos 'data:image/png;base64,'.
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

// -------------------------------------------------------------------------
// Endpoint: POST /gerar-qrcode-dinamico
// - Aceita multipart/form-data contendo:
//    - 'Text' (string) obrigatório
//    - 'Logo' (arquivo de imagem) opcional
// - Se um logo for enviado, ele será centralizado sobre o QR Code com uma
//   pequena margem e com nível de correção de erros ECC mais alto (H) para
//   aumentar a chance de leitura do QR Code mesmo com o logo sobreposto.
// -------------------------------------------------------------------------
app.MapPost("/gerar-qrcode-dinamico",
   async ([FromForm] QrCodeRequest request) =>
    {
        if (string.IsNullOrEmpty(request.Text))
        {
            return Results.BadRequest("O parâmetro 'texto' é obrigatório.");
        }

        try
        {
            // Ajusta o nível ECC dependendo se há um logo (H = mais tolerância a danos).
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
                        // Gera o QR Code como PNG em bytes.
                        qrCodeImageBytes = qrCode.GetGraphic(20);
                    }
                }
            }

            // Se não houver logo, retornamos o PNG gerado diretamente.
            if (request.Logo == null || request.Logo.Length == 0)
            {
                return Results.File(qrCodeImageBytes, "image/png");
            }

            // Caso exista logo, abrimos a imagem do QR Code e desenhamos o logo centralizado.
            using (Image qrTemp = Image.Load(qrCodeImageBytes))
            {
                // Cria uma 'tela' (canvas) com fundo branco para desenhar o QR Code e o logo.
                using (var canvas = new Image<Rgba32>(qrTemp.Width, qrTemp.Height, Color.White))
                {
                    using (Image qrDrawable = Image.Load(qrCodeImageBytes))
                    {
                        canvas.Mutate(x => x.DrawImage(qrDrawable, new Point(0, 0), 1f));
                    }

                    // Carrega a imagem do logo enviada pelo usuário.
                    using (Image logoImage = await Image.LoadAsync(request.Logo.OpenReadStream()))
                    {
                        // Define o tamanho do logo (1/5 da largura do QR Code) e redimensiona.
                        int logoSize = canvas.Width / 4;

                        logoImage.Mutate(x => x.Resize(logoSize, logoSize));

                        int logoXPos = (canvas.Width - logoImage.Width) / 2;
                        int logoYPos = (canvas.Height - logoImage.Height) / 2;

                        int padding = logoSize / 10; // margem em pixels ao redor do logo

                        // Desenha um retângulo branco de fundo para garantir contraste sobre o QR Code.
                        RectangleF backgroundArea = new RectangleF(
                            logoXPos - padding,
                            logoYPos - padding,
                            logoImage.Width + (padding * 2),
                            logoImage.Height + (padding * 2)
                        );

                        canvas.Mutate(x => x.Fill(Color.White, backgroundArea));

                        // Desenha o logo sobre o QR Code já no centro.
                        canvas.Mutate(x => x.DrawImage(logoImage, new Point(logoXPos, logoYPos), 1f));
                    }

                    // Converte o canvas final para PNG e retorna como arquivo.
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
            // Erro específico quando o arquivo não é uma imagem válida.
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

app.MapPost("/gerar-qrcode-estilizado",
    async([FromForm] EstiloQrRequest request) =>
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

            QRCodeData qrCodeData;
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            {
                qrCodeData = qrGenerator.CreateQrCode(request.Text, eccLevel);
            }

            int pixelPerModule = 20;
            var moduleMatrix = qrCodeData.ModuleMatrix;
            int qrSize = moduleMatrix.Count * pixelPerModule;

            var corFundo = Color.Parse(request.CorFundo);
            var corModulos = Color.Parse(request.CorModulos);
            var brushModulo = Brushes.Solid(corModulos);

            string estilo = request.EstiloModulos.ToLowerInvariant();

            using (var canvas = new Image<Rgba32>(qrSize, qrSize, corFundo))
            {
                for (int x = 0; x < moduleMatrix.Count; x++)
                {
                    for (int y = 0; y < moduleMatrix.Count; y++)
                    {
                        if (moduleMatrix[x][y])
                        {
                            float xPos = x * pixelPerModule;
                            float yPos = y * pixelPerModule;
                            float radius = pixelPerModule / 2;

                            switch (estilo)
                            {
                                case "ponto":
                                    canvas.Mutate(ctx => ctx.Fill(brushModulo,
                                        new EllipsePolygon(xPos + radius, yPos + radius, radius)
                                        ));
                                    break;
                                case "arredondado":
                                    radius = pixelPerModule / 2;
                                    float centerX = xPos + radius;
                                    float centerY = yPos + radius;

                                    canvas.Mutate(ctx => ctx.Fill(brushModulo,
                                        new EllipsePolygon(centerX, centerY, radius)
                                    ));

                                    if (x + 1 < moduleMatrix.Count && moduleMatrix[x + 1][y])
                                    {
                                        canvas.Mutate(ctx => ctx.Fill(brushModulo,
                                            new RectangleF(centerX, yPos, pixelPerModule, pixelPerModule)
                                        ));
                                    }

                                    if (y + 1 < moduleMatrix.Count && moduleMatrix[x][y + 1])
                                    {
                                        canvas.Mutate(ctx => ctx.Fill(brushModulo,
                                            new RectangleF(xPos, centerY, pixelPerModule, pixelPerModule)
                                        ));
                                    }
                                    break;
                                case "quadrado":
                                default:
                                    canvas.Mutate(ctx => ctx.Fill(brushModulo,
                                        new RectangleF(xPos, yPos, pixelPerModule, pixelPerModule)
                                        ));
                                    break;
                            }
                        }
                    }
                }

                if (request.Logo != null && request.Logo.Length > 0)
                {
                    using (Image logoImage = await Image.LoadAsync(request.Logo.OpenReadStream()))
                    {
                        int logoSize = canvas.Width / 4;
                        logoImage.Mutate(x => x.Resize(logoSize, logoSize));

                        int logoXPos = (canvas.Width - logoImage.Width) / 2;
                        int logoYPos = (canvas.Height - logoImage.Height) / 2;

                        int padding = logoSize / 10;

                        RectangleF backgroundArea = new RectangleF(
                            logoXPos - padding,
                            logoYPos - padding,
                            logoImage.Width + (padding * 2),
                            logoImage.Height + (padding * 2)
                         );

                        canvas.Mutate(x => x.Fill(Color.White, backgroundArea));

                        canvas.Mutate(x => x.DrawImage(logoImage, new Point(logoXPos, logoYPos), 1f));
                    }
                }

                using (var ms = new MemoryStream())
                {
                    await canvas.SaveAsPngAsync(ms);
                    return Results.File(ms.ToArray(), "image/png");
                }
            }

        }
        catch(Exception ex)
        {
            if (ex is FormatException)
            {
                return Results.BadRequest("Erro: CorModulos ou CorFundo está em formato inválido. Use o formato hexadecimal (ex: '#FF0000').");
            }
            if (ex.Message.Contains("Image cannot be loaded"))
            {
                return Results.BadRequest("Erro: O arquivo enviado não é uma imagem válida (PNG, JPG, etc.).");
            }
            return Results.Problem($"Erro ao gerar QR Code: {ex.Message}", statusCode: 500);
        }
    }).WithName("GerarQrCodeEstilizadoPost")
      .DisableAntiforgery() // Necessário para [FromForm] com classe
      .WithDescription("Gera um QR Code em PNG com logo opcional, cores e estilos de módulo (quadrado, arredondado, ponto).");

app.Run();

/// <summary>
/// Representa os dados esperados no endpoint de geração dinâmica de QR Code.
/// - Text: texto que será codificado no QR Code (obrigatório)
/// - Logo: arquivo de imagem opcional (PNG/JPG) que será centralizado sobre o QR Code
/// </summary>
public class QrCodeRequest
{
    /// <summary>
    /// Texto a ser codificado no QR Code.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Arquivo de imagem enviado via multipart/form-data. Opcional.
    /// </summary>
    public IFormFile? Logo { get; set; }
}

public class EstiloQrRequest
{
    // Texto para o QR Code (obrigatório)
    public string Text { get; set; }

    // Logo opcional
    public IFormFile? Logo { get; set; }

    // Estilo dos módulos: "quadrado", "arredondado", "ponto"
    public string EstiloModulos { get; set; } = "quadrado";

    // Cor principal (ex: "#000000")
    public string CorModulos { get; set; } = "#000000";

    // Cor do fundo (ex: "#FFFFFF")
    public string CorFundo { get; set; } = "#FFFFFF";
}