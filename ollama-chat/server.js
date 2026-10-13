const express = require('express');
const cors = require('cors');
const path = require('path');

const app = express();
const PORT = process.env.PORT || 3000;

// Ollama endpoint — меняется в зависимости от развёртывания
const OLLAMA_URL = process.env.OLLAMA_URL || 'http://176.53.172.57:11434';
const MODEL = process.env.MODEL || 'llama3.2:3b';

app.use(cors());
app.use(express.json());
app.use(express.static(path.join(__dirname, 'public')));

// Health check
app.get('/health', (req, res) => {
  res.json({ status: 'ok', ollama: OLLAMA_URL, model: MODEL });
});

// Chat API
app.post('/api/chat', async (req, res) => {
  const { messages } = req.body;

  if (!messages || !Array.isArray(messages) || messages.length === 0) {
    return res.status(400).json({ error: 'Messages array is required' });
  }

  try {
    const response = await fetch(`${OLLAMA_URL}/api/chat`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        model: MODEL,
        messages: messages,
        stream: false
      })
    });

    if (!response.ok) {
      const errorText = await response.text();
      return res.status(response.status).json({
        error: 'Ollama error',
        details: errorText
      });
    }

    const data = await response.json();
    res.json(data);
  } catch (err) {
    res.status(502).json({
      error: 'Backend error',
      details: err.message
    });
  }
});

// Streaming chat
app.post('/api/chat/stream', async (req, res) => {
  const { messages } = req.body;

  if (!messages || !Array.isArray(messages) || messages.length === 0) {
    return res.status(400).json({ error: 'Messages array is required' });
  }

  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');

  try {
    const response = await fetch(`${OLLAMA_URL}/api/chat`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        model: MODEL,
        messages: messages,
        stream: true
      })
    });

    const reader = response.body.getReader();
    const decoder = new TextDecoder();

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      const chunk = decoder.decode(value, { stream: true });
      const lines = chunk.split('\n').filter(l => l.trim());

      for (const line of lines) {
        try {
          const data = JSON.parse(line);
          res.write(`data: ${JSON.stringify(data)}\n\n`);
        } catch (e) {
          // Skip non-JSON lines
        }
      }
    }

    res.write('data: [DONE]\n\n');
    res.end();
  } catch (err) {
    res.write(`data: {"error":"${err.message}"}\n\n`);
    res.end();
  }
});

app.listen(PORT, '0.0.0.0', () => {
  console.log(`Chat server running on port ${PORT}`);
  console.log(`  -> http://localhost:${PORT}`);
  console.log(`  -> Ollama: ${OLLAMA_URL}`);
  console.log(`  -> Model: ${MODEL}`);
});
