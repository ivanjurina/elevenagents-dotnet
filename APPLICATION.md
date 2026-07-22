# Developer Relations Engineer application — ElevenLabs

## where to send
Apply through the posting: https://elevenlabs.io/careers → Developer Relations Engineer.
If there's a cover-letter / "why you" field, paste the body below. If it's just a resume
upload, put the first two paragraphs + the links in the message box.

## links to have ready
- repo: https://github.com/ivanjurina/elevenagents-dotnet
- nuget: https://www.nuget.org/packages/ElevenAgents.Net
- nuget (SK bridge): https://www.nuget.org/packages/ElevenAgents.Net.SemanticKernel
- blog post: https://www.ivanjurina.com/index.php/2026/07/22/giving-a-net-app-a-voice-building-on-the-elevenlabs-agents-platform/
- prior work (serpapi campaign): the 3 merged/open PRs + SerpClient.Net + that blog post

---

## application body

hi ElevenLabs team,

i'm applying for the developer community growth role. the job is about meeting developers where they are and helping them succeed, so instead of describing that, i did it for a segment you don't cover yet: .net.

you ship agents sdks for python, typescript, kotlin and swift. there's no .net one. so a .net developer who wants to build on ElevenAgents has to hand-roll the websocket protocol. i built the thing that should exist:

- **ElevenAgents.Net** — a realtime client for the agents platform. websocket conversations, transcripts, interruptions, automatic ping/pong, client tools, signed-url auth. net8.0, zero dependencies, async-first. live on nuget.
- **ElevenAgents.Net.SemanticKernel** — a bridge that turns any Semantic Kernel function into an ElevenLabs client tool. one line, and your agent can call your existing .net code. i tested it end to end: asked a voice agent "what's the status of order 1234?" and watched it call my c# method and speak the answer back.
- **a blog post** walking .net developers through the protocol and the build, including a gotcha i hit (the websocket serves the published agent, not your draft) that i turned into docs so nobody else loses that hour.

that last part is the actual job, i think. i didn't just write code, i found the friction and wrote the thing that removes it.

about me: 15 years of software engineering, currently a .net/azure consultant in prague building multi-agent ai systems for a bank (Azure AI Foundry, Semantic Kernel, Claude API). i write at ivanjurina.com and i use python and javascript daily alongside c#, so this isn't a .net-only pitch. it's me showing i can open a developer audience you're not reaching, and produce the content and demos to bring them in.

full disclosure, because your "how we work" section values it: i lean hard on ai coding tools, which is how one person ships two libraries, tests, samples and a writeup this fast. you list that as a bonus, so i'm being upfront that it's how i work.

one more data point on range: i just ran the same play for SerpApi (found their neglected .net client, sent prs, built a modern replacement, wrote it up). so this isn't a one-off. finding an underserved developer segment and building its way in is just what i do. i'd like to do it for you with a login.

happy to transfer the packages and repo to your org if you want them official.

ivan jurina
ivanjurina.com · linkedin.com/in/ivanjurina · github.com/ivanjurina
