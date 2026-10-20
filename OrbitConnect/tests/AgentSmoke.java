import io.netty.bootstrap.ServerBootstrap;
import io.netty.channel.*;
import io.netty.channel.nio.NioEventLoopGroup;
import io.netty.channel.socket.nio.NioServerSocketChannel;
import java.net.*;
import java.io.*;
import com.sun.net.httpserver.HttpServer;
import net.minecraft.client.multiplayer.resolver.ServerAddress;

public final class AgentSmoke {
    public static void main(String[] args) throws Exception {
        EventLoopGroup group = new NioEventLoopGroup(1);
        Channel channel = null;
        try {
            channel = new ServerBootstrap().group(group).channel(NioServerSocketChannel.class)
                .childHandler(new ChannelInboundHandlerAdapter()).bind("127.0.0.1", 0).sync().channel();
            int port = ((InetSocketAddress)channel.localAddress()).getPort();
            String bind = System.getProperty("orbit.connect.lan", "");
            if (!bind.endsWith(":" + port)) throw new AssertionError("Actual Netty LAN port not captured: " + bind);
            System.out.println("PASS agent: actual Netty bind port captured, transformed class verifies");
            if (!ServerAddress.parseString("example.com:25565").address.equals("example.com:25565")) throw new AssertionError("Normal server address changed");
            final ServerSocket echo = new ServerSocket(0, 1, InetAddress.getByName("127.0.0.1"));
            Thread server = new Thread(() -> {
                try (Socket client = echo.accept()) {
                    byte[] bytes = new byte[8]; new DataInputStream(client.getInputStream()).readFully(bytes);
                    client.getOutputStream().write(bytes);
                } catch (IOException e) { throw new RuntimeException(e); }
            }); server.setDaemon(true); server.start();
            HttpServer control = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 8);
            final java.util.concurrent.atomic.AtomicInteger targetPort = new java.util.concurrent.atomic.AtomicInteger(echo.getLocalPort());
            final java.util.concurrent.atomic.AtomicBoolean idle = new java.util.concurrent.atomic.AtomicBoolean(true);
            final java.util.concurrent.atomic.AtomicInteger published = new java.util.concurrent.atomic.AtomicInteger();
            control.createContext("/", exchange -> {
                if (!"Bearer fixture-token".equals(exchange.getRequestHeaders().getFirst("Authorization"))) { exchange.sendResponseHeaders(403, -1); exchange.close(); return; }
                if (exchange.getRequestURI().getPath().equals("/lan")) published.incrementAndGet();
                String phase = idle.get() && exchange.getRequestURI().getPath().equals("/state") ? "idle" : "connected";
                byte[] bytes = ("{\"Phase\":\"" + phase + "\",\"Address\":\"127.0.0.1:" + targetPort.get() + "\"}").getBytes("UTF-8");
                exchange.sendResponseHeaders(200, bytes.length); exchange.getResponseBody().write(bytes); exchange.close();
            }); control.start();
            try {
                System.setProperty("orbit.connect.api", "http://127.0.0.1:" + control.getAddress().getPort() + "/");
                System.setProperty("orbit.connect.token", "fixture-token");
                Class.forName("org.orbitlauncher.connect.Bridge", true, null).getMethod("start", java.lang.instrument.Instrumentation.class).invoke(null, new Object[]{null});
                Thread.sleep(2400);
                if (published.get() != 1) throw new AssertionError("Manual stop auto-published the same LAN generation " + published.get() + " times");
                idle.set(false);
                System.out.println("PASS agent: manual stop is respected until a new LAN bind");
                long started = System.nanoTime();
                String resolved = ServerAddress.parseString("orbit1:fixture").address;
                if (!new net.minecraft.client.multiplayer.ServerData("orbit1:fixture").isLan()) throw new AssertionError("Own invitation lost ordinary LAN semantics");
                if (new net.minecraft.client.multiplayer.ServerData("example.com").isLan()) throw new AssertionError("Public server marked LAN");
                if (!resolved.startsWith("127.0.0.1:") || System.nanoTime() - started > 500_000_000L) throw new AssertionError("Invitation blocks game thread");
                try (Socket socket = new Socket("127.0.0.1", Integer.parseInt(resolved.substring(10)))) {
                    socket.setSoTimeout(5000); byte[] bytes = {1,2,3,4,5,6,7,8}, reply = new byte[8];
                    socket.getOutputStream().write(bytes); new DataInputStream(socket.getInputStream()).readFully(reply);
                    if (!java.util.Arrays.equals(bytes, reply)) throw new AssertionError("Invite proxy corrupted bytes");
                }
                System.out.println("PASS agent: unchanged regular address, immediate invite resolution, authenticated async IPC and real stream");
                echo.close();
                try (ServerSocket replacement = new ServerSocket(0, 1, InetAddress.getByName("127.0.0.1"))) {
                    targetPort.set(replacement.getLocalPort());
                    Thread replaced = new Thread(() -> { try (Socket client = replacement.accept()) { client.getOutputStream().write(42); } catch (IOException ignored) { } });
                    replaced.setDaemon(true); replaced.start();
                    String again = ServerAddress.parseString("orbit1:fixture").address;
                    try (Socket socket = new Socket("127.0.0.1", Integer.parseInt(again.substring(10)))) {
                        socket.setSoTimeout(5000); if (socket.getInputStream().read() != 42) throw new AssertionError("Stopped invitation reused stale target");
                    }
                }
                System.out.println("PASS agent: same invitation reconnects after worker target is replaced");
                String generation = System.getProperty("orbit.connect.lan.generation", "");
                channel.close().sync();
                // Java 8 on Windows can retain a closed NIO socket until its selector is released.
                group.shutdownGracefully(0, 2, java.util.concurrent.TimeUnit.SECONDS).sync();
                group = new NioEventLoopGroup(1);
                channel = new ServerBootstrap().group(group).channel(NioServerSocketChannel.class).childHandler(new ChannelInboundHandlerAdapter()).bind("127.0.0.1", port).sync().channel();
                if (generation.isEmpty() || generation.equals(System.getProperty("orbit.connect.lan.generation", ""))) throw new AssertionError("Reopening same LAN port did not create a new generation");
                System.out.println("PASS agent: reopening the same port creates a fresh LAN generation");
            } finally { control.stop(0); echo.close(); }
        } finally { if (channel != null) channel.close().sync(); group.shutdownGracefully().sync(); }
    }
}
