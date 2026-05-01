import { WebSocket } from 'ws';

const KEY = 'REPLACE_ME_DEV_ONLY_64HEX';
const URL = `ws://localhost:8080/tts/stream?api_key=${KEY}`;
const ws = new WebSocket(URL);

let pcmBytes = 0;
let segStarts = 0;
let segEnds = 0;
const start = Date.now();
let firstAudioAt = null;

ws.on('open', () => {
  console.log('open');
  ws.send(JSON.stringify({ type: 'config', voice: 'af_heart', speed: 1.0 }));
  ws.send(JSON.stringify({ type: 'text', delta: 'Hello from Kokoro running inside dotnet 10.' }));
  ws.send(JSON.stringify({ type: 'flush' }));
});

ws.on('message', (data, isBinary) => {
  if (isBinary) {
    if (!firstAudioAt) firstAudioAt = Date.now() - start;
    pcmBytes += data.length;
  } else {
    const m = JSON.parse(data.toString());
    if (m.type === 'segment_start') { segStarts++; console.log('seg_start', m.id, JSON.stringify(m.text)); }
    else if (m.type === 'segment_end') { segEnds++; console.log('seg_end', m.id); }
    else console.log(m);
  }
});

ws.on('close', () => {
  const total = Date.now() - start;
  console.log(`done: pcmBytes=${pcmBytes} (${(pcmBytes/2/24000).toFixed(2)}s of audio) starts=${segStarts} ends=${segEnds} firstAudio=${firstAudioAt}ms total=${total}ms`);
  process.exit(0);
});

ws.on('error', e => { console.error('ERR', e.message); process.exit(1); });

setTimeout(() => { ws.close(); }, 30000);
