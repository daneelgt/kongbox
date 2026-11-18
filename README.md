# KONGBOX

Transforma um celular Android antigo num painel de mesa e num deck de controle por toque para o seu PC.
O celular roda um app em tela cheia; um agente pequeno no Windows serve os layouts, coleta os dados ao vivo
e executa as ações. Os layouts são montados num editor visual no navegador. Sem nuvem, sem conta, sem telemetria.

Visual inspirado na Apple (calmo, tipografia grande, curvas de animação do iOS), com alma de dev:
rótulos `// cpu`, prompt `kong@pc ›`, cursor piscando, números monoespaçados.

```
┌──────────── PC (Windows) ─────────────┐          USB (adb reverse)          ┌──── celular ────┐
│ KongBoxAgent  127.0.0.1:8765          │ ◄──────────────────────────────────► │ app KONGBOX     │
│  · editor visual  (/)                 │                                      │ WebView em tela │
│  · painel         (/panel/)           │                                      │ cheia → /panel/ │
│  · API: estado, ações por id, webhook │                                      └─────────────────┘
└───────────────────────────────────────┘
```

## O que já faz

| Área | Funcionalidades |
|---|---|
| **Monitor do PC** | CPU, GPU (NVIDIA via `nvidia-smi`; outras via contadores do Windows), RAM, VRAM, discos, rede, ping, gráficos de 2 min, processos com "encerrar um / todos" |
| **IA & código** | Cotas do Claude (sessão 5 h, semanal, Opus/Sonnet) e do Codex, horário do reset e **previsão de consumo** ("no ritmo atual: cheio em 1 h 20"); sessões locais do Claude Code e do Codex com modelo, duração e tokens; **prompts de permissão do Claude Code no celular** (Permitir / Negar); PRs e CI do GitHub |
| **Toque** | Atalhos estilo Stream Deck: programa, URL, `ms-settings:`, combinação de teclas, texto digitado, macros, botões on/off; tocando agora com capa, seek e controles; volume e mudo |
| **Dia a dia** | Relógio, analógico, mundial, por extenso, calendário, progresso do tempo, contagem regressiva, pomodoro, timer, cronômetro, clima, previsão, sol & lua, lua, qualidade do ar, nota editável pelo celular, checklist diário, contador, respiração, frase do dia, bateria do celular |
| **Automação** | Webhooks (`/api/push`, `/api/notify`, `/api/goto`), notificações estilo Dynamic Island com vibração, regras ("CPU > 90% por 45 s → avisar / trocar página / rodar atalho") |
| **Layout** | 46 widgets, 20 templates, páginas numa grade 12 × 6, **Smart Stack** (até 12 widgets num espaço), retrato e paisagem, preview ao vivo das alterações não salvas, estilos Vidro / Plano / Sem borda, cor de destaque, raio e espaçamento |
| **Celular** | Swipe entre páginas, pontos e visão geral (toque longo), teclas de volume trocam de página, volta à home quando ocioso, modo noturno com relógio que se move, proteção contra burn-in, tela sempre ligada, funciona do Android 5.0 em diante |

## Rodando

Ferramentas (nesta máquina ficam em `E:\DevTools`): .NET 10 SDK, JDK 17, Android SDK (platform 35) e adb.

```powershell
.\scripts\build.ps1          # gera dist\agent\KongBoxAgent.exe e dist\KongBox.apk
.\dist\agent\KongBoxAgent.exe --open
```

1. No celular: Opções do desenvolvedor › **Depuração USB**, conecte o cabo e aceite a chave.
2. Em <http://127.0.0.1:8765> › **Celular** › *Instalar app* › *Abrir KONGBOX*.
3. Monte as páginas em **Páginas**; o celular atualiza em segundos.

## Webhooks

```sh
curl -X POST http://127.0.0.1:8765/api/push/build -H "Content-Type: application/json" -d '{"value":"passed","sub":"main · 3m"}'
curl -X POST http://127.0.0.1:8765/api/notify -H "Content-Type: application/json" -d '{"title":"Deploy","text":"No ar","level":"ok"}'
curl -X POST http://127.0.0.1:8765/api/goto/music
```

## Segurança

- O agente escuta só em `127.0.0.1`. Ele recusa `Host` diferente (DNS rebinding), `Origin` de outro site e
  ações sem o header `X-Kong` (CSRF).
- O celular só envia **ids**. Comandos, tokens e caminhos ficam no `config.json` do PC
  (`%APPDATA%\KongBox`) e nunca vão para o painel.
- A ponte JavaScript do app exige uma chave entregue só ao frame principal; o widget de HTML próprio roda num
  iframe sandbox sem acesso a ela.
- Serviços externos usados: Open-Meteo, Frankfurter, CoinGecko, Hacker News (via o agente, com cache),
  `api.anthropic.com` (só para ler a sua própria cota, desligável), GitHub (se configurado).

## Estrutura

```
agent/KongBox.Agent/      agente .NET 10: servidor, monitores, ações, editor (settings.html)
android/app/              app Kotlin; assets/www é o painel (também embutido no agente)
android/app/src/main/assets/www/widgets/   widgets em JS puro, por categoria
scripts/build.ps1         build do agente e do APK
docs/ROADMAP.md           o que vem nas próximas fases
```
