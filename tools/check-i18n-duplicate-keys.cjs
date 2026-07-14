const fs = require('node:fs')
const path = require('node:path')

const localesDirectory = path.resolve(__dirname, '..', 'app', 'src', 'i18n', 'locales')
const localeFiles = fs.readdirSync(localesDirectory).filter((file) => file.endsWith('.json')).sort()
let failed = false

function parseJsonWithDuplicateCheck(text, file) {
  let position = 0

  function error(message) {
    throw new SyntaxError(`${message} at position ${position}`)
  }

  function skipWhitespace() {
    while (/\s/.test(text[position] ?? '')) position += 1
  }

  function parseString() {
    const start = position
    if (text[position] !== '"') error('Expected string')
    position += 1
    let escaped = false
    while (position < text.length) {
      const character = text[position]
      position += 1
      if (escaped) {
        escaped = false
      } else if (character === '\\') {
        escaped = true
      } else if (character === '"') {
        return JSON.parse(text.slice(start, position))
      }
    }
    error('Unterminated string')
  }

  function parseValue() {
    skipWhitespace()
    const character = text[position]
    if (character === '{') return parseObject()
    if (character === '[') return parseArray()
    if (character === '"') return parseString()
    if (text.startsWith('true', position)) { position += 4; return true }
    if (text.startsWith('false', position)) { position += 5; return false }
    if (text.startsWith('null', position)) { position += 4; return null }
    const number = text.slice(position).match(/^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/)
    if (number) {
      position += number[0].length
      return Number(number[0])
    }
    error('Unexpected value')
  }

  function parseObject() {
    position += 1
    const keys = new Set()
    const value = {}
    skipWhitespace()
    if (text[position] === '}') { position += 1; return value }
    while (position < text.length) {
      skipWhitespace()
      const key = parseString()
      if (keys.has(key)) {
        console.error(`${file}: duplicate key "${key}"`)
        failed = true
      }
      keys.add(key)
      skipWhitespace()
      if (text[position] !== ':') error('Expected colon')
      position += 1
      value[key] = parseValue()
      skipWhitespace()
      if (text[position] === '}') { position += 1; return value }
      if (text[position] !== ',') error('Expected comma')
      position += 1
    }
    error('Unterminated object')
  }

  function parseArray() {
    position += 1
    const value = []
    skipWhitespace()
    if (text[position] === ']') { position += 1; return value }
    while (position < text.length) {
      value.push(parseValue())
      skipWhitespace()
      if (text[position] === ']') { position += 1; return value }
      if (text[position] !== ',') error('Expected comma')
      position += 1
    }
    error('Unterminated array')
  }

  const value = parseValue()
  skipWhitespace()
  if (position !== text.length) error('Unexpected trailing content')
  return value
}

for (const file of localeFiles) {
  const filePath = path.join(localesDirectory, file)
  try {
    parseJsonWithDuplicateCheck(fs.readFileSync(filePath, 'utf8'), file)
  } catch (error) {
    console.error(`${file}: ${error instanceof Error ? error.message : String(error)}`)
    failed = true
  }
}

if (failed) process.exitCode = 1