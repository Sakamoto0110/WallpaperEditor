# ADR 0003 — Toda publicação é pré-renderizada na resolução nativa

**Status:** Aceita · **Data:** 2026-11-06

## Contexto

`IDesktopWallpaper` aceita uma imagem por monitor, mas o modo de ajuste é **global**:
`SetPosition` não recebe monitor. Se o app dependesse disso, escolher "preencher" em um monitor
mudaria os outros, o que contradiz o objetivo do projeto.

Nesta máquina, com monitores de 16:9, 21:9 e 9:16, cada um provavelmente precisa de um ajuste
diferente.

## Decisão

O app **nunca entrega ao Windows uma imagem que precise de ajuste**. Toda publicação, Static ou
Dynamic:

1. Lê a resolução física atual do monitor.
2. Renderiza a origem nesse tamanho exato, aplicando o ajuste escolhido no próprio app.
3. Grava um **PNG** com **nome novo**, em `%LOCALAPPDATA%\WallpaperEditor\published\`.
4. Chama `SetWallpaper` com esse arquivo.

`SetPosition` **não é chamado**, salvo se o usuário aceitar sair do modo span.

## Por quê

- **Independência:** com a imagem do tamanho exato, centralizar, ajustar, preencher e esticar
  produzem o mesmo resultado. O ajuste por monitor passa a existir, mesmo sem a API oferecê-lo. O
  modo span é a exceção, e por isso é detectado antes de publicar. O teste T6 confirma o caso do
  monitor vertical.
- **PNG:** passa intacto pelo Windows (0 pixels diferentes, teste T5), sem mexer no registro do usuário.
- **Nome novo:** `SetWallpaper` é assíncrono, e o shell lê o arquivo depois que a chamada retorna. Um nome novo
  evita sobrescrever um arquivo em uso. A suspeita inicial de cache preso ao caminho não se confirmou (teste T4).
- **Pasta própria:** mover ou apagar um projeto, ou o arquivo original, não afeta o que está na tela.

## Alternativas

| Opção | Por que não |
|---|---|
| Passar o arquivo original e usar `SetPosition` | O modo seria global, e JPEGs seriam recomprimidos |
| Alterar `JPEGImportQuality` | Altera configuração do sistema e não resolve o modo global |
| Sobrescrever sempre o mesmo arquivo | Condição de corrida com o shell, que lê o arquivo de forma assíncrona |

## Consequências

- O custo é de dois PNGs por monitor em uso, de alguns MiB cada: o atual e o anterior, que pode ainda estar sendo
  lido pelo shell durante trocas rápidas.
- **Se a resolução ou a orientação do monitor mudar, a imagem publicada fica incorreta.** O app
  precisa detectar isso (por `WM_DISPLAYCHANGE` quando aberto, e ao iniciar) e oferecer republicar.
- A cópia em cache é idêntica à imagem enviada (teste T5), e o quadriculado de 1 px chega intacto à tela (teste T6).
