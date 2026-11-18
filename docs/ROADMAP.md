# Roadmap

Arquitetura: agente .NET no PC + app Android com WebView + widgets em JS puro.
A fase 1 entregou o núcleo de ponta a ponta. Próximas fases:

## Fase 2: Control Deck (≈ 60 widgets)
- Mixer por aplicativo, volume por dispositivo, balanço, medidores, microfone, seleção de entrada/saída,
  push-to-talk, perfis de áudio, ducking, botão de pânico (Core Audio por sessão)
- Mídia: fontes, seek, shuffle/repeat, sleep timer, fila e favoritos do Spotify (OAuth PKCE)
- Sistema: brilho, planos de energia, desktops virtuais, bloquear/monitores, desligar com segurar-para-confirmar
- Produtividade: dock de apps, hotkeys, macro pad, layouts de janela, project launcher, snippets, apresentação,
  perfis de foco/reunião, timer de reunião, controles de reunião
- Dev: task runner (build/test com saída), dev servers, serviços, GitHub workflows, script pad
- Som: soundboard, rádio online (diretório Radio Browser, HLS), playlists, ouvir o áudio do PC no celular
- MIDI, OSC, mixer externo (Voicemeeter), roteamento por app (SoundVolumeView)

## Fase 3: integrações
- OBS (obs-websocket): cenas, fontes, mixer, gravação/stream, replay buffer, transição, câmera virtual, "on air"
- Discord (RPC local), Home Assistant, Docker, câmeras RTSP via ffmpeg, porta-retrato, feeds RSS/Atom,
  agenda ICS (Google/Outlook/iCloud) e "próximo evento", ditado (fala no celular → digitado no PC),
  app console por janela focada, baterias de periféricos, battery care
- CodexBar e cotas nativas do Antigravity

## Fase 4: conexão e distribuição
- Wi-Fi com HTTPS na 8766, certificado fixado e pareamento por QR + código de verificação; Tailscale
- Vários celulares e vários PCs (widget "Computadores")
- Atualização do agente via GitHub Releases com SHA-256; atualização do app por adb
- Instaladores (setup.ps1, winget, Scoop), início com o sistema já existe
- Agente para Linux e macOS (playerctl/wpctl, nowplaying-cli)
- Sensores do celular (brilho automático, acordar com aceno), fixar tela, painel como tela inicial
- Traduções (inglês, espanhol e outros idiomas)
