package org.orbitlauncher.connect;

import java.io.*;
import java.net.*;
import java.lang.instrument.Instrumentation;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;

/** Private loopback IPC, bounded daemon tasks and asynchronous invitation resolution. */
public final class Bridge {
    private static Instrumentation instrumentation;
    private static final Map<String, JoinProxy> invites = new LinkedHashMap<String, JoinProxy>();
    private static final ExecutorService IO = Executors.newCachedThreadPool(r -> { Thread t = new Thread(r, "Orbit Connect I/O"); t.setDaemon(true); return t; });
    private static final AtomicInteger connections = new AtomicInteger();
    private static volatile boolean stopped;
    private static String invitation = "";
    private static volatile String localAddress = "";
    public static void start(Instrumentation value) {
        instrumentation = value;
        if (System.getProperty("orbit.connect.api", "").isEmpty()) return;
        IO.execute(() -> {
            try {
                String pid = java.lang.management.ManagementFactory.getRuntimeMXBean().getName().split("@")[0];
                request("attach", "{\"pid\":" + Integer.parseInt(pid) + "}");
            } catch (Throwable ignored) { }
            String announced = "";
            while (!stopped) {
                try {
                    String bind = System.getProperty("orbit.connect.lan", "");
                    String generation = bind + "|" + System.getProperty("orbit.connect.lan.generation", "");
                    Map<String,Object> status = request("state", null);
                    String nextPhase = string(status, "Phase");
                    localAddress = nextPhase.equals("connected") ? string(status, "Address") : "";
                    if (!bind.isEmpty() && !generation.equals(announced)) {
                        int port = Integer.parseInt(bind.substring(bind.lastIndexOf(':') + 1));
                        Map<String,Object> result = request("lan", "{\"port\":" + port + "}");
                        if (!string(result, "Phase").equals("error")) announced = generation;
                    }
                    String code = string(status, "Invitation");
                    if (!code.isEmpty() && !code.equals(invitation)) { invitation = code; notifyGame(code); }
                } catch (Throwable ignored) { }
                try { Thread.sleep(1000); } catch (InterruptedException end) { return; }
            }
        });
    }
    public static synchronized String resolve(String code) {
        if (code != null && (code.trim().startsWith("orbit1:") || code.trim().toUpperCase(Locale.ROOT).startsWith("U/"))) code = code.trim();
        if (code == null || (!code.startsWith("orbit1:") && !code.toUpperCase(Locale.ROOT).startsWith("U/"))) return code;
        if (code.length() > 4096 || System.getProperty("orbit.connect.api", "").isEmpty()) return code;
        try {
            JoinProxy proxy = invites.get(code);
            if (proxy != null && proxy.listener.isClosed()) { invites.remove(code); proxy = null; }
            if (proxy == null) {
                if (invites.size() >= 4) { Iterator<JoinProxy> old = invites.values().iterator(); old.next().close(); old.remove(); }
                proxy = new JoinProxy(code); proxy.listen(); invites.put(code, proxy);
            }
            return "127.0.0.1:" + proxy.listener.getLocalPort();
        } catch (Throwable ignored) { return code; }
    }
    public static synchronized void markLan(Object data) {
        if (data == null) return;
        try {
            String address = "";
            for (String name : new String[]{"ip", "address", "field_3761", "serverIP"}) try { address = String.valueOf(data.getClass().getField(name).get(data)).trim(); break; } catch (NoSuchFieldException ignored) { }
            // Preserve vanilla LAN behavior only for invitations resolved by this adapter or our owned local endpoint.
            if (!invites.containsKey(address) && (localAddress.isEmpty() || !address.equals(localAddress))) return;
            for (Field field : data.getClass().getDeclaredFields()) if (field.getType().isEnum() && !Modifier.isFinal(field.getModifiers())) {
                for (Object value : field.getType().getEnumConstants()) if (((Enum<?>)value).name().equals("LAN")) { field.setAccessible(true); field.set(data, value); return; }
            }
        } catch (Throwable ignored) { }
    }
    private static Map<String,Object> request(String action, String body) throws Exception {
        URI base = URI.create(System.getProperty("orbit.connect.api", ""));
        if (!"http".equals(base.getScheme()) || !"127.0.0.1".equals(base.getHost()) || base.getPort() < 1) throw new IOException("Loopback only");
        HttpURLConnection connection = (HttpURLConnection)base.resolve(action).toURL().openConnection(java.net.Proxy.NO_PROXY);
        try {
            connection.setConnectTimeout(1200); connection.setReadTimeout(1500); connection.setInstanceFollowRedirects(false);
            connection.setRequestProperty("Authorization", "Bearer " + System.getProperty("orbit.connect.token", ""));
            if (body != null) {
                byte[] bytes = body.getBytes("UTF-8"); connection.setRequestMethod("POST"); connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/json"); connection.setFixedLengthStreamingMode(bytes.length);
                try (OutputStream output = connection.getOutputStream()) { output.write(bytes); }
            }
            if (connection.getResponseCode() != 200) throw new IOException("Control unavailable");
            ByteArrayOutputStream output = new ByteArrayOutputStream(); byte[] buffer = new byte[2048];
            try (InputStream input = connection.getInputStream()) {
                int n; while ((n = input.read(buffer)) != -1) { if (output.size() + n > 16384) throw new IOException("Control size"); output.write(buffer, 0, n); }
            }
            return Json.object(Json.read(new String(output.toByteArray(), "UTF-8")));
        } finally { connection.disconnect(); }
    }
    private static String string(Map<String,Object> value, String key) { Object item = value.get(key); return item instanceof String ? (String)item : ""; }
    private static String quote(String text) {
        StringBuilder out = new StringBuilder("\"");
        for (char ch : text.toCharArray()) { if (ch == '"' || ch == '\\') out.append('\\'); if (ch < 32) out.append(String.format("\\u%04x", (int)ch)); else out.append(ch); }
        return out.append('"').toString();
    }
    private static final class JoinProxy implements AutoCloseable {
        final ServerSocket listener;
        final String code;
        JoinProxy(final String code) throws IOException {
            this.code = code;
            listener = new ServerSocket(); listener.bind(new InetSocketAddress("127.0.0.1", 0), 8);
        }
        private InetSocketAddress target() throws Exception {
                    Map<String,Object> state = request("join", "{\"code\":" + quote(code) + "}");
                    String room = string(state, "RoomId");
                    long until = System.nanoTime() + TimeUnit.SECONDS.toNanos(55);
                    while (!string(state,"Phase").equals("connected")) {
                        if (string(state,"Phase").equals("error") || !room.equals(string(state,"RoomId")) || System.nanoTime() >= until || listener.isClosed()) throw new IOException("Join failed");
                        Thread.sleep(200); state = request("state", null);
                    }
                    String address = string(state,"Address");
                    if (!address.startsWith("127.0.0.1:")) throw new IOException("Loopback only");
                    int port = Integer.parseInt(address.substring(10)); if (port < 1 || port > 65535) throw new IOException("Port");
                    return new InetSocketAddress("127.0.0.1", port);
        }
        void listen() {
            IO.execute(() -> {
                while (!listener.isClosed()) {
                    try {
                        final Socket game = listener.accept();
                        if (connections.incrementAndGet() > 8) { connections.decrementAndGet(); game.close(); continue; }
                        IO.execute(() -> forward(game));
                    } catch (IOException end) { break; }
                }
            });
        }
        private void forward(Socket game) {
            try (Socket local = game; Socket remote = new Socket()) {
                game.setTcpNoDelay(true); remote.setTcpNoDelay(true); remote.connect(target(), 1500);
                Future<?> upload = IO.submit(() -> copy(game, remote));
                copy(remote, game); upload.get();
            } catch (Throwable ignored) { try { game.close(); } catch (IOException end) { } }
            finally { connections.decrementAndGet(); }
        }
        private static void copy(Socket from, Socket to) {
            try { byte[] bytes = new byte[16384]; int n; InputStream input = from.getInputStream(); OutputStream output = to.getOutputStream();
                while ((n = input.read(bytes)) != -1) output.write(bytes, 0, n); to.shutdownOutput();
            } catch (IOException error) { try { from.close(); to.close(); } catch (IOException ignored) { } }
        }
        public void close() { try { listener.close(); } catch (IOException ignored) { } }
    }
    private static void notifyGame(final String code) {
        if (instrumentation == null) return;
        for (final Class<?> type : instrumentation.getAllLoadedClasses()) {
            if (!type.getName().equals("net.minecraft.client.Minecraft") && !type.getName().equals("net.minecraft.client.MinecraftClient") && !type.getName().equals("net.minecraft.class_310")) continue;
            try {
                Object client = null;
                for (Method method : type.getDeclaredMethods()) if (Modifier.isStatic(method.getModifiers()) && method.getParameterTypes().length == 0 && method.getReturnType() == type) { method.setAccessible(true); client = method.invoke(null); if (client != null) break; }
                final Object game = client; if (game == null) continue;
                Runnable task = () -> { try { chat(type, game, code); } catch (Throwable ignored) { } };
                for (String name : new String[]{"execute", "submit", "addScheduledTask", "func_152344_a", "method_18859"}) {
                    try { type.getMethod(name, Runnable.class).invoke(game, task); return; } catch (NoSuchMethodException ignored) { }
                }
            } catch (Throwable ignored) { }
        }
    }
    private static void chat(Class<?> type, Object game, String code) throws Exception {
        ClassLoader loader = type.getClassLoader();
        boolean english = "en".equals(System.getProperty("orbit.connect.language", "ru"));
        String label = english ? "Orbit Connect · World open. [Copy invitation]" : "Orbit Connect · Мир открыт. [Скопировать приглашение]";
        Object text = literal(loader, label);
        if (text == null) return;
        try {
            Object click;
            try {
                Class<?> event = Class.forName("net.minecraft.network.chat.ClickEvent$CopyToClipboard", false, loader);
                click = event.getConstructor(String.class).newInstance(code);
            } catch (ClassNotFoundException oldVersion) {
                Class<?> action = Class.forName("net.minecraft.network.chat.ClickEvent$Action", false, loader);
                Object copy = action.getField("COPY_TO_CLIPBOARD").get(null);
                click = Class.forName("net.minecraft.network.chat.ClickEvent", false, loader).getConstructor(action, String.class).newInstance(copy, code);
            }
            Class<?> styleType = Class.forName("net.minecraft.network.chat.Style", false, loader);
            Object style = styleType.getField("EMPTY").get(null);
            try { style = styleType.getMethod("withoutShadow").invoke(style); } catch (NoSuchMethodException oldVersion) { }
            try { style = styleType.getMethod("withColor", int.class).invoke(style, Integer.valueOf(0xB2A4F9)); } catch (NoSuchMethodException oldVersion) { }
            style = styleType.getMethod("withClickEvent", Class.forName("net.minecraft.network.chat.ClickEvent", false, loader)).invoke(style, click);
            text.getClass().getMethod("setStyle", styleType).invoke(text, style);
        } catch (ReflectiveOperationException ignored) {
            text = literal(loader, english ? "Orbit Connect · World open. Copy the invitation in the launcher panel." : "Orbit Connect · Мир открыт. Приглашение можно скопировать в панели лаунчера.");
        }
        if (text == null) return;
        Object gui = null;
        for (String name : new String[]{"gui", "inGameHud", "field_1705", "ingameGUI", "field_71456_v"}) try { gui = type.getField(name).get(game); break; } catch (NoSuchFieldException ignored) { }
        if (gui == null) return;
        try { gui = gui.getClass().getField("hud").get(gui); } catch (NoSuchFieldException oldVersion) { }
        Object hud = null;
        for (String name : new String[]{"getChat", "getChatHud", "method_1743", "getChatGUI", "func_146158_b"}) try { hud = gui.getClass().getMethod(name).invoke(gui); break; } catch (NoSuchMethodException ignored) { }
        if (hud == null) return;
        for (Method method : hud.getClass().getMethods()) if ((method.getName().equals("addClientSystemMessage") || method.getName().equals("addMessage") || method.getName().equals("method_1812") || method.getName().equals("printChatMessage") || method.getName().equals("func_146227_a")) && method.getParameterTypes().length == 1 && method.getParameterTypes()[0].isInstance(text)) { method.invoke(hud, text); return; }
    }
    private static Object literal(ClassLoader loader, String label) {
        Object text = null;
        for (String name : new String[]{"net.minecraft.network.chat.Component", "net.minecraft.text.Text", "net.minecraft.class_2561"}) {
            try { Class<?> component = Class.forName(name, false, loader);
                for (String factory : new String[]{"literal", "method_43470"}) try { text = component.getMethod(factory, String.class).invoke(null, label); break; } catch (NoSuchMethodException ignored) { }
                if (text != null) break;
            } catch (ReflectiveOperationException ignored) { }
        }
        if (text == null) for (String name : new String[]{"net.minecraft.util.text.TextComponentString", "net.minecraft.text.LiteralText", "net.minecraft.class_2585"}) {
            try { text = Class.forName(name, false, loader).getConstructor(String.class).newInstance(label); break; } catch (ReflectiveOperationException ignored) { }
        }
        return text;
    }
    private Bridge() { }
}
