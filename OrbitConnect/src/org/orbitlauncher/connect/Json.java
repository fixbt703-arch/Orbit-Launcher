package org.orbitlauncher.connect;

import java.util.*;

/** Small bounded JSON reader; no external JSON library is exposed to the game. */
final class Json {
    private final String text; private int pos;
    private Json(String text) { if (text.length() > 196608) throw new IllegalArgumentException("JSON size"); this.text = text; }
    static Object read(String text) { Json reader = new Json(text); Object value = reader.value(0); reader.space(); if (reader.pos != text.length()) throw new IllegalArgumentException("Trailing JSON"); return value; }
    private void space() { while (pos < text.length() && Character.isWhitespace(text.charAt(pos))) pos++; }
    private char take() { if (pos >= text.length()) throw new IllegalArgumentException("Truncated JSON"); return text.charAt(pos++); }
    private Object value(int depth) {
        if (depth > 32) throw new IllegalArgumentException("JSON depth"); space(); char ch = take();
        if (ch == '"') return string();
        if (ch == '{') {
            Map<String,Object> result = new LinkedHashMap<>(); space(); if (pos < text.length() && text.charAt(pos) == '}') { pos++; return result; }
            do { space(); if (take() != '"') throw new IllegalArgumentException("JSON key"); String key = string(); space(); if (take() != ':') throw new IllegalArgumentException("JSON separator"); result.put(key, value(depth + 1)); space(); ch = take(); } while (ch == ',');
            if (ch != '}') throw new IllegalArgumentException("JSON object"); return result;
        }
        if (ch == '[') {
            List<Object> result = new ArrayList<>(); space(); if (pos < text.length() && text.charAt(pos) == ']') { pos++; return result; }
            do { result.add(value(depth + 1)); space(); ch = take(); } while (ch == ',');
            if (ch != ']') throw new IllegalArgumentException("JSON array"); return result;
        }
        int start = pos - 1; while (pos < text.length() && ",]} \n\r\t".indexOf(text.charAt(pos)) < 0) pos++;
        String atom = text.substring(start, pos); if (atom.equals("true")) return Boolean.TRUE; if (atom.equals("false")) return Boolean.FALSE; if (atom.equals("null")) return null;
        return Double.valueOf(atom);
    }
    private String string() {
        StringBuilder out = new StringBuilder();
        while (true) {
            char ch = take(); if (ch == '"') return out.toString(); if (ch < 32) throw new IllegalArgumentException("JSON control");
            if (ch == '\\') {
                ch = take(); switch (ch) {
                    case '"': case '\\': case '/': break;
                    case 'b': ch = '\b'; break; case 'f': ch = '\f'; break; case 'n': ch = '\n'; break; case 'r': ch = '\r'; break; case 't': ch = '\t'; break;
                    case 'u': if (pos + 4 > text.length()) throw new IllegalArgumentException("JSON unicode"); ch = (char)Integer.parseInt(text.substring(pos, pos + 4), 16); pos += 4; break;
                    default: throw new IllegalArgumentException("JSON escape");
                }
            }
            out.append(ch);
        }
    }
    @SuppressWarnings("unchecked") static Map<String,Object> object(Object value) { if (!(value instanceof Map)) throw new IllegalArgumentException("Expected object"); return (Map<String,Object>)value; }
    static String string(Object value) { if (!(value instanceof String)) throw new IllegalArgumentException("Expected string"); return (String)value; }
}

