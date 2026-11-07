# ADR 0001 — WPF sobre .NET 10

**Status:** Aceita · **Data:** 2026-11-06

## Contexto

O app tem um editor visual: manipular uma imagem na tela com alças, ver o histórico e listar
monitores. Só existe para Windows, porque tudo o que faz depende de APIs do Windows.

## Decisão

**WPF** sobre **.NET 10** (`net10.0-windows`), que é a versão LTS e já é usada em outros projetos
deste diretório, como o `AudioVisualizer`.

## Alternativas

| Opção | Por que não |
|---|---|
| WinUI 3 | Mais moderno visualmente, mas com ferramentas e empacotamento menos maduros, sem ganho para este caso |
| Avalonia | O principal benefício é ser multiplataforma, o que é inútil para um app que só faz sentido no Windows |
| WinForms | Fraco para uma interface de edição com transformações e bindings |
| C++ com Win32 | Muito mais trabalho na interface, sem necessidade: publicar é uma ação pontual, e o WPF já recompõe uma cena em cerca de 0,5 s ([ADR 0005](0005-renderizacao-com-wpf.md)) |

## Consequências

- XAML, bindings e `Canvas` atendem bem às alças de manipulação.
- O WPF também gera a imagem final, sem dependências adicionais ([ADR 0005](0005-renderizacao-com-wpf.md)).
- O WPF não é usado no modo animado: o processo host não tem interface
  ([ADR 0006](0006-reproducao-de-video.md)).
