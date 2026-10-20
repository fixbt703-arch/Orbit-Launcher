package org.orbitlauncher.connect;

import java.io.File;
import java.lang.instrument.*;
import java.security.ProtectionDomain;
import java.util.jar.JarFile;
import org.objectweb.asm.*;

/** Loader-independent LAN hook. Networking runs in a daemon, never in the bind method. */
public final class Agent {
    public static void premain(String options, Instrumentation instrumentation) {
        try {
            File file = new File(Agent.class.getProtectionDomain().getCodeSource().getLocation().toURI());
            instrumentation.appendToBootstrapClassLoaderSearch(new JarFile(file));
            instrumentation.addTransformer(new Hooks());
            Class.forName("org.orbitlauncher.connect.Bridge", true, null).getMethod("start", Instrumentation.class).invoke(null, instrumentation);
            System.out.println("[Orbit Connect] LAN adapter enabled; network helpers run outside Minecraft.");
        } catch (Throwable error) { System.err.println("[Orbit Connect] Adapter unavailable; use launcher panel."); }
    }
    public static final class Hooks implements ClassFileTransformer {
        public byte[] transform(ClassLoader loader, final String name, Class<?> type, ProtectionDomain domain, byte[] bytes) {
            final boolean lan = "io/netty/channel/socket/nio/NioServerSocketChannel".equals(name);
            boolean address = "net/minecraft/class_639".equals(name) || "net/minecraft/client/multiplayer/resolver/ServerAddress".equals(name)
                || "net/minecraft/client/multiplayer/ServerAddress".equals(name);
            final boolean serverData = "net/minecraft/client/multiplayer/ServerData".equals(name) || "net/minecraft/class_642".equals(name);
            if (!lan && !address && !serverData) return null;
            try {
                ClassReader reader = new ClassReader(bytes);
                ClassWriter writer = new ClassWriter(reader, ClassWriter.COMPUTE_MAXS);
                final int[] changes = {0};
                reader.accept(new ClassVisitor(Opcodes.ASM9, writer) {
                    public MethodVisitor visitMethod(int access, String method, String desc, String signature, String[] exceptions) {
                        MethodVisitor original = super.visitMethod(access, method, desc, signature, exceptions);
                        if (serverData && desc.equals("()Z") && (method.equals("isLan") || method.equals("isOnLAN") || method.equals("isLanServer"))) {
                            changes[0]++;
                            return new MethodVisitor(Opcodes.ASM9, original) {
                                public void visitCode() {
                                    super.visitCode();
                                    Label begin = new Label(), end = new Label(), fail = new Label(), done = new Label();
                                    super.visitTryCatchBlock(begin, end, fail, "java/lang/Throwable"); super.visitLabel(begin);
                                    super.visitLdcInsn("org.orbitlauncher.connect.Bridge"); super.visitInsn(Opcodes.ICONST_1); super.visitInsn(Opcodes.ACONST_NULL);
                                    super.visitMethodInsn(Opcodes.INVOKESTATIC, "java/lang/Class", "forName", "(Ljava/lang/String;ZLjava/lang/ClassLoader;)Ljava/lang/Class;", false);
                                    super.visitLdcInsn("markLan"); super.visitInsn(Opcodes.ICONST_1); super.visitTypeInsn(Opcodes.ANEWARRAY, "java/lang/Class");
                                    super.visitInsn(Opcodes.DUP); super.visitInsn(Opcodes.ICONST_0); super.visitLdcInsn(Type.getType(Object.class)); super.visitInsn(Opcodes.AASTORE);
                                    super.visitMethodInsn(Opcodes.INVOKEVIRTUAL, "java/lang/Class", "getMethod", "(Ljava/lang/String;[Ljava/lang/Class;)Ljava/lang/reflect/Method;", false);
                                    super.visitInsn(Opcodes.ACONST_NULL); super.visitInsn(Opcodes.ICONST_1); super.visitTypeInsn(Opcodes.ANEWARRAY, "java/lang/Object");
                                    super.visitInsn(Opcodes.DUP); super.visitInsn(Opcodes.ICONST_0); super.visitVarInsn(Opcodes.ALOAD, 0); super.visitInsn(Opcodes.AASTORE);
                                    super.visitMethodInsn(Opcodes.INVOKEVIRTUAL, "java/lang/reflect/Method", "invoke", "(Ljava/lang/Object;[Ljava/lang/Object;)Ljava/lang/Object;", false); super.visitInsn(Opcodes.POP);
                                    super.visitLabel(end); super.visitJumpInsn(Opcodes.GOTO, done);
                                    super.visitLabel(fail); super.visitFrame(Opcodes.F_SAME1, 0, null, 1, new Object[]{"java/lang/Throwable"}); super.visitInsn(Opcodes.POP);
                                    super.visitLabel(done); super.visitFrame(Opcodes.F_SAME, 0, null, 0, null);
                                }
                            };
                        }
                        if (lan && method.equals("doBind") && desc.equals("(Ljava/net/SocketAddress;)V")) {
                            changes[0]++;
                            return new MethodVisitor(Opcodes.ASM9, original) {
                                public void visitInsn(int opcode) {
                                    if (opcode == Opcodes.RETURN) {
                                        // Only JDK/Netty references enter game bytecode; no foreign ASM/classloader linkage.
                                        Label begin = new Label(), end = new Label(), fail = new Label(), done = new Label();
                                        super.visitTryCatchBlock(begin, end, fail, "java/lang/Throwable"); super.visitLabel(begin);
                                        super.visitLdcInsn("orbit.connect.lan"); super.visitVarInsn(Opcodes.ALOAD, 0);
                                        super.visitMethodInsn(Opcodes.INVOKEVIRTUAL, "io/netty/channel/AbstractChannel", "localAddress", "()Ljava/net/SocketAddress;", false);
                                        super.visitMethodInsn(Opcodes.INVOKEVIRTUAL, "java/lang/Object", "toString", "()Ljava/lang/String;", false);
                                        super.visitMethodInsn(Opcodes.INVOKESTATIC, "java/lang/System", "setProperty", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", false);
                                        super.visitInsn(Opcodes.POP);
                                        super.visitLdcInsn("orbit.connect.lan.generation");
                                        super.visitMethodInsn(Opcodes.INVOKESTATIC, "java/lang/System", "nanoTime", "()J", false);
                                        super.visitMethodInsn(Opcodes.INVOKESTATIC, "java/lang/Long", "toString", "(J)Ljava/lang/String;", false);
                                        super.visitMethodInsn(Opcodes.INVOKESTATIC, "java/lang/System", "setProperty", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", false);
                                        super.visitInsn(Opcodes.POP); super.visitLabel(end); super.visitJumpInsn(Opcodes.GOTO, done);
                                        super.visitLabel(fail); super.visitFrame(Opcodes.F_SAME1, 0, null, 1, new Object[]{"java/lang/Throwable"}); super.visitInsn(Opcodes.POP);
                                        super.visitLabel(done); super.visitFrame(Opcodes.F_SAME, 0, null, 0, null);
                                    }
                                    super.visitInsn(opcode);
                                }
                            };
                        }
                        if (!lan && (access & Opcodes.ACC_STATIC) != 0 && desc.equals("(Ljava/lang/String;)L" + name + ";")) {
                            changes[0]++;
                            return new MethodVisitor(Opcodes.ASM9, original) {
                                public void visitCode() {
                                    super.visitCode();
                                    Label begin = new Label(), end = new Label(), fail = new Label(), done = new Label();
                                    super.visitTryCatchBlock(begin, end, fail, "java/lang/Throwable"); super.visitLabel(begin);
                                    super.visitLdcInsn("org.orbitlauncher.connect.Bridge"); super.visitInsn(Opcodes.ICONST_1); super.visitInsn(Opcodes.ACONST_NULL);
                                    super.visitMethodInsn(Opcodes.INVOKESTATIC, "java/lang/Class", "forName", "(Ljava/lang/String;ZLjava/lang/ClassLoader;)Ljava/lang/Class;", false);
                                    super.visitLdcInsn("resolve"); super.visitInsn(Opcodes.ICONST_1); super.visitTypeInsn(Opcodes.ANEWARRAY, "java/lang/Class");
                                    super.visitInsn(Opcodes.DUP); super.visitInsn(Opcodes.ICONST_0); super.visitLdcInsn(Type.getType(String.class)); super.visitInsn(Opcodes.AASTORE);
                                    super.visitMethodInsn(Opcodes.INVOKEVIRTUAL, "java/lang/Class", "getMethod", "(Ljava/lang/String;[Ljava/lang/Class;)Ljava/lang/reflect/Method;", false);
                                    super.visitInsn(Opcodes.ACONST_NULL); super.visitInsn(Opcodes.ICONST_1); super.visitTypeInsn(Opcodes.ANEWARRAY, "java/lang/Object");
                                    super.visitInsn(Opcodes.DUP); super.visitInsn(Opcodes.ICONST_0); super.visitVarInsn(Opcodes.ALOAD, 0); super.visitInsn(Opcodes.AASTORE);
                                    super.visitMethodInsn(Opcodes.INVOKEVIRTUAL, "java/lang/reflect/Method", "invoke", "(Ljava/lang/Object;[Ljava/lang/Object;)Ljava/lang/Object;", false);
                                    super.visitTypeInsn(Opcodes.CHECKCAST, "java/lang/String"); super.visitVarInsn(Opcodes.ASTORE, 0);
                                    super.visitLabel(end); super.visitJumpInsn(Opcodes.GOTO, done);
                                    super.visitLabel(fail); super.visitFrame(Opcodes.F_SAME1, 0, null, 1, new Object[]{"java/lang/Throwable"}); super.visitInsn(Opcodes.POP);
                                    super.visitLabel(done); super.visitFrame(Opcodes.F_SAME, 0, null, 0, null);
                                }
                            };
                        }
                        return original;
                    }
                }, 0);
                return changes[0] == 0 ? null : writer.toByteArray();
            } catch (Throwable error) { System.err.println("[Orbit Connect] Optional game hook skipped."); return null; }
        }
    }
}
