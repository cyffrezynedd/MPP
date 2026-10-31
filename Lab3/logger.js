const fs = require('fs');
const path = require('path');

const logsDir = path.join(__dirname, 'logs');
if (!fs.existsSync(logsDir)) {
  fs.mkdirSync(logsDir, { recursive: true });
}

const logFile = path.join(logsDir, 'app.log');
const logStream = fs.createWriteStream(logFile, { flags: 'a' });

function writeLog(level, message, meta = {}) {
  const entry = {
    timestamp: new Date().toISOString(),
    level,
    message,
    ...meta
  };
  const json = JSON.stringify(entry);
  process.stdout.write(json + '\n');
  logStream.write(json + '\n');
}

const logger = {
  info: (msg, meta) => writeLog('INFO', msg, meta),
  warn: (msg, meta) => writeLog('WARN', msg, meta),
  error: (msg, meta) => writeLog('ERROR', msg, meta),
  security: (msg, meta) => writeLog('SECURITY', msg, meta)
};

module.exports = logger;