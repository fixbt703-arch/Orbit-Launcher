package org.orbitlauncher.skinbridge;

import java.io.File;
import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.Instrumentation;
import java.security.ProtectionDomain;
import java.util.jar.JarFile;
import java.util.LinkedHashMap;
import java.util.Map;
import org.objectweb.asm.*;

/** Adds texture fallbacks only. No login, session join, URL or public-key transformer. */
public final class Agent {
    private static final String BRIDGE = "org/orbitlauncher/skinbridge/Bridge";
    public static void premain(String options, Instrumentation instrumentation) throws Exception {
        File jar = new File(Agent.class.getProtectionDomain().getCodeSource().getLocation().toURI());
        instrumentation.appendToBootstrapClassLoaderSearch(new JarFile(jar));
        instrumentation.addTransformer(new Textures());
        System.out.println("[Orbit skins] Texture-only Ely.by support enabled.");
    }
    public static final class Textures implements ClassFileTransformer {
        public byte[] transform(ClassLoader loader, String name, Class<?> type, ProtectionDomain domain, byte[] bytes) {
            if (name == null || (!name.equals("com/mojang/authlib/GameProfile") &&
                !(name.startsWith("com/mojang/authlib/") && name.endsWith("SessionService")))) return null;
            try {
                // Resolve through bootstrap explicitly. Knot intentionally rejects direct references
                // to agent classes outside its game classpath, even if they are bootstrap-visible.
                Class.forName(BRIDGE.replace('/', '.'), true, null);
                ClassReader reader = new ClassReader(bytes);
                ClassWriter writer = new ClassWriter(reader, ClassWriter.COMPUTE_MAXS);
                final int[] hooks = {0};
                Map<String, String> handles = new LinkedHashMap<>();
                // Collect before visiting <clinit>; it may precede the methods using these handles.
                reader.accept(new ClassVisitor(Opcodes.ASM9) {
                    public MethodVisitor visitMethod(int access, String method, String desc, String sig, String[] errors) {
                        if (name.equals("com/mojang/authlib/GameProfile") && method.equals("<init>"))
                            handles.put("remember", "(Ljava/util/UUID;Ljava/lang/String;)V");
                        if (method.equals("getPackedTextures")) handles.put("packed", "(Ljava/lang/Object;Ljava/lang/Object;Ljava/lang/Class;)Ljava/lang/Object;");
                        if (method.equals("fetchProfile")) handles.put("profile", "(Ljava/lang/Object;Ljava/lang/Object;Ljava/lang/Class;)Ljava/lang/Object;");
                        if (method.equals("unpackTextures")) handles.put("unpack", "(Ljava/lang/Object;Ljava/lang/Class;)Ljava/lang/Object;");
                        if (method.equals("getTextures")) handles.put("legacy", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;");
                        return null;
                    }
                }, ClassReader.SKIP_CODE | ClassReader.SKIP_DEBUG | ClassReader.SKIP_FRAMES);
                reader.accept(new ClassVisitor(Opcodes.ASM9, writer) {
                    private boolean initializer;
                    private void initialize(MethodVisitor mv) {
                        for (Map.Entry<String, String> entry : handles.entrySet()) {
                            mv.visitMethodInsn(Opcodes.INVOKESTATIC, "java/lang/invoke/MethodHandles", "publicLookup", "()Ljava/lang/invoke/MethodHandles$Lookup;", false);
                            mv.visitLdcInsn(BRIDGE.replace('/', '.'));
                            mv.visitInsn(Opcodes.ICONST_1); mv.visitInsn(Opcodes.ACONST_NULL);
                            mv.visitMethodInsn(Opcodes.INVOKESTATIC, "java/lang/Class", "forName", "(Ljava/lang/String;ZLjava/lang/ClassLoader;)Ljava/lang/Class;", false);
                            mv.visitLdcInsn(entry.getKey()); mv.visitLdcInsn(entry.getValue()); mv.visitInsn(Opcodes.ACONST_NULL);
                            mv.visitMethodInsn(Opcodes.INVOKESTATIC, "java/lang/invoke/MethodType", "fromMethodDescriptorString", "(Ljava/lang/String;Ljava/lang/ClassLoader;)Ljava/lang/invoke/MethodType;", false);
                            mv.visitMethodInsn(Opcodes.INVOKEVIRTUAL, "java/lang/invoke/MethodHandles$Lookup", "findStatic", "(Ljava/lang/Class;Ljava/lang/String;Ljava/lang/invoke/MethodType;)Ljava/lang/invoke/MethodHandle;", false);
                            mv.visitFieldInsn(Opcodes.PUTSTATIC, name, "orbit$skinbridge$" + entry.getKey(), "Ljava/lang/invoke/MethodHandle;");
                        }
                    }
                    public void visitEnd() {
                        for (String hook : handles.keySet()) super.visitField(Opcodes.ACC_PRIVATE | Opcodes.ACC_STATIC | Opcodes.ACC_FINAL | Opcodes.ACC_SYNTHETIC,
                            "orbit$skinbridge$" + hook, "Ljava/lang/invoke/MethodHandle;", null, null).visitEnd();
                        if (!initializer && !handles.isEmpty()) {
                            MethodVisitor mv = super.visitMethod(Opcodes.ACC_STATIC, "<clinit>", "()V", null, null);
                            mv.visitCode(); initialize(mv); mv.visitInsn(Opcodes.RETURN); mv.visitMaxs(0, 0); mv.visitEnd();
                        }
                        super.visitEnd();
                    }
                    public MethodVisitor visitMethod(int access, String method, String descriptor, String signature, String[] errors) {
                        MethodVisitor base = super.visitMethod(access, method, descriptor, signature, errors);
                        if (method.equals("<clinit>")) {
                            initializer = true;
                            return new MethodVisitor(Opcodes.ASM9, base) {
                                public void visitCode() { super.visitCode(); initialize(this); }
                            };
                        }
                        if ((access & (Opcodes.ACC_ABSTRACT | Opcodes.ACC_STATIC | Opcodes.ACC_NATIVE)) != 0) return base;
                        Type[] args = Type.getArgumentTypes(descriptor);
                        Type result = Type.getReturnType(descriptor);
                        boolean remember = name.equals("com/mojang/authlib/GameProfile") && method.equals("<init>") && args.length >= 2 &&
                            args[0].getDescriptor().equals("Ljava/util/UUID;") && args[1].getDescriptor().equals("Ljava/lang/String;");
                        boolean packed = method.equals("getPackedTextures") && args.length == 1 && args[0].getClassName().equals("com.mojang.authlib.GameProfile") && result.getClassName().equals("com.mojang.authlib.properties.Property");
                        boolean unpack = method.equals("unpackTextures") && args.length == 1 && args[0].getClassName().equals("com.mojang.authlib.properties.Property") && result.getClassName().equals("com.mojang.authlib.minecraft.MinecraftProfileTextures");
                        boolean profile = method.equals("fetchProfile") && args.length == 2 && args[0].getDescriptor().equals("Ljava/util/UUID;") && result.getClassName().endsWith(".ProfileResult");
                        boolean legacy = method.equals("getTextures") && args.length == 2 && args[0].getClassName().equals("com.mojang.authlib.GameProfile") && result.getDescriptor().equals("Ljava/util/Map;");
                        if (!remember && !packed && !unpack && !profile && !legacy) return base;
                        hooks[0]++;
                        return new MethodVisitor(Opcodes.ASM9, base) {
                            public void visitCode() {
                                super.visitCode();
                                if (!unpack && !legacy) return;
                                handle(legacy ? "legacy" : "unpack");
                                if (legacy) super.visitInsn(Opcodes.ACONST_NULL);
                                super.visitVarInsn(Opcodes.ALOAD, 1);
                                if (!legacy) super.visitLdcInsn(result);
                                invoke(legacy ? "legacy" : "unpack");
                                super.visitInsn(Opcodes.DUP);
                                Label vanilla = new Label();
                                super.visitJumpInsn(Opcodes.IFNULL, vanilla);
                                super.visitTypeInsn(Opcodes.CHECKCAST, result.getInternalName());
                                super.visitInsn(Opcodes.ARETURN);
                                super.visitLabel(vanilla);
                                super.visitFrame(Opcodes.F_SAME1, 0, null, 1, new Object[] {"java/lang/Object"});
                                super.visitInsn(Opcodes.POP);
                            }
                            public void visitInsn(int opcode) {
                                if (remember && opcode == Opcodes.RETURN) {
                                    handle("remember");
                                    super.visitVarInsn(Opcodes.ALOAD, 1); super.visitVarInsn(Opcodes.ALOAD, 2);
                                    invoke("remember");
                                }
                                if (opcode == Opcodes.ARETURN && (packed || profile || legacy)) {
                                    handle(legacy ? "legacy" : packed ? "packed" : "profile");
                                    super.visitInsn(Opcodes.SWAP);
                                    super.visitVarInsn(Opcodes.ALOAD, 1);
                                    if (legacy) {
                                        invoke("legacy");
                                    } else {
                                        super.visitLdcInsn(result);
                                        invoke(packed ? "packed" : "profile");
                                    }
                                    super.visitTypeInsn(Opcodes.CHECKCAST, result.getInternalName());
                                }
                                super.visitInsn(opcode);
                            }
                            private void handle(String hook) {
                                super.visitFieldInsn(Opcodes.GETSTATIC, name, "orbit$skinbridge$" + hook, "Ljava/lang/invoke/MethodHandle;");
                            }
                            private void invoke(String hook) {
                                super.visitMethodInsn(Opcodes.INVOKEVIRTUAL, "java/lang/invoke/MethodHandle", "invokeExact", handles.get(hook), false);
                            }
                        };
                    }
                }, 0);
                if (hooks[0] == 0) return null;
                System.out.println("[Orbit skins] Texture hooks: " + name + " (" + hooks[0] + ")");
                return writer.toByteArray();
            } catch (Exception | LinkageError failure) {
                System.err.println("[Orbit skins] Unsupported texture class; keeping vanilla behavior.");
                return null;
            }
        }
    }
}
