# ADR 0005 — Renderização com WPF, sem biblioteca de imagem externa

**Status:** Aceita · **Data:** 2026-11-06

## Contexto

O app precisa desenhar fundo, imagens (com posição, tamanho, rotação e opacidade) e checklists em um único
bitmap, e salvar como PNG. "Extremamente leve" é um requisito.

> **Correção.** A primeira versão escolhia SkiaSharp por causa da inserção em perspectiva. Os exemplos
> reais não precisam de perspectiva, e precisam de **texto com quebra de linha**, que o SkiaSharp não
> oferece pronto. O motivo da escolha deixou de existir.

## Decisão

Usar o próprio WPF (`DrawingVisual` + `RenderTargetBitmap` + `PngBitmapEncoder`), dentro do projeto
`WallpaperEditor.Rendering`. O restante do código vê apenas a interface definida no Core.

## Medição

**[Medido em 2026-11-06]**, com um protótipo descartável, 5 execuções a frio em processos separados,
sem nenhuma janela, nesta máquina.

Cena em 2560×1080: fundo JPEG de 6000×3375, uma imagem girada, 55 linhas de código em Consolas 15 px
e uma checklist com 6 tarefas.

| Métrica | Resultado |
|---|---|
| Tempo total para compor, renderizar e codificar | 461 a 484 ms |
| Desde o início do processo | 522 a 587 ms |
| Pico de memória do processo | 90 a 92 MiB |
| PNG gerado | 2,22 MiB |

As imagens eram sintéticas, com gradientes e pouco ruído. Uma foto real deve gerar um PNG maior, e o
tempo de codificação tende a crescer junto. Texto em 15 px ficou nítido na inspeção visual, em tamanho real.

## Por quê

- **Nenhuma dependência nova.** O WPF já está no processo, o que significa menos para carregar e
  menos arquivos para distribuir.
- **Texto pronto para as checklists.** `FormattedText` oferece quebra de linha, fonte de fallback e tachado.
- **Um só caminho de desenho.** O editor exibe o mesmo `DrawingVisual` que é publicado, então o preview é
  fiel sem esforço adicional.
- **Decodificação via WIC.** `DecodePixelWidth` evita carregar a foto inteira na memória.

## Alternativas

| Opção | Por que não |
|---|---|
| SkiaSharp | Dependência nativa adicional, e texto com quebra de linha precisaria ser implementado por nós. Só compensaria com perspectiva ou efeitos, o que não é pedido. Reavaliado ao discutir uma biblioteca para "injetar" imagens: desenhar uma imagem dentro da outra é exatamente o que o WPF já faz aqui |
| ImageSharp | Licença com restrições comerciais, e sem vantagem aqui |
| Direct2D + DirectWrite | Mais rápido, mas muito mais código por um ganho irrelevante: publicar é uma ação pontual, e 0,5 s já basta |

## Consequências

Encontradas durante o protótipo **[Medido]**, e tratadas como requisitos:

- **`InvariantGlobalization` não pode ser usado.** Ativar essa opção, que às vezes é sugerida para reduzir
  o tamanho do app, faz o WPF falhar ao desenhar texto (`CultureNotFoundException` em `MajorLanguages`).
- **Conteúdo precisa de recorte.** Sem isso, o texto ultrapassa a caixa quando não cabe. Cada item é recortado
  ao próprio retângulo.
- **Caixas de seleção são desenhadas como geometria.** Os caracteres `☑` e `☐` vêm de fonte de fallback
  e ficam com tamanhos diferentes.

Encontradas na implementação **[Medido]**:

- **O WPF ignora a orientação EXIF.** Sem tratamento, fotos de celular apareceriam deitadas. `LoadedImage` lê a
  orientação e aplica a rotação, e o ajuste usa o tamanho já corrigido.
- **`BitmapCreateOptions.IgnoreImageCache` falha com `StreamSource`** (`ArgumentNullException`). O arquivo é lido por
  stream, o que já evita o cache de imagens do WPF, indexado por URI.
- **`BitmapScalingMode.HighQuality` preserva os pixels em 1:1,** portanto um cheatsheet em tamanho real não borra. O
  modo Centralizar arredonda a posição para pixels inteiros, para continuar em 1:1 quando a diferença de tamanho é
  ímpar.
- **Bitmaps decodificados continuam presos à thread do decoder, mesmo congelados.** Para cruzar threads, copie os
  pixels para um novo bitmap. `RenderTargetBitmap` congelado não tem esse problema.

Outras:

- `Rendering` precisa de uma thread STA dedicada, e os testes também, pois `DrawingVisual` exige STA.
- `Rendering` passa a depender do WPF (`net10.0-windows`). O modelo de dados continua no Core, sem WPF.
- Trocar de biblioteca depois fica restrito a um único projeto.
