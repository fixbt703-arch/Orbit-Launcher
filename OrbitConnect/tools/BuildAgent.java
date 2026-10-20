import java.nio.file.*;
import java.io.*;
import java.util.*;
import java.util.jar.*;
import org.objectweb.asm.*;
import org.objectweb.asm.commons.*;

/** Deterministic packaging; shade ASM so its classes cannot conflict with Fabric/Forge. */
public class BuildAgent {
    public static void main(String[] args) throws Exception {
        Path classes = Path.of(args[0]), output = Path.of(args[1]);
        Map<String,byte[]> files = new TreeMap<>();
        try (var paths = Files.walk(classes)) {
            for (Path path : paths.filter(Files::isRegularFile).toList()) files.put(classes.relativize(path).toString().replace('\\', '/'), Files.readAllBytes(path));
        }
        try (JarFile asm = new JarFile(args[2])) {
            for (JarEntry entry : Collections.list(asm.entries())) if (entry.getName().startsWith("org/objectweb/asm/") && entry.getName().endsWith(".class")) {
                files.put(entry.getName(), asm.getInputStream(entry).readAllBytes());
            }
        }
        files.put("META-INF/MANIFEST.MF", ("Manifest-Version: 1.0\r\nPremain-Class: org.orbitlauncher.connect.Agent\r\nImplementation-Version: 1.0.1\r\n\r\n").getBytes(java.nio.charset.StandardCharsets.UTF_8));
        files.put("META-INF/LICENSE-ASM.txt", Files.readAllBytes(Path.of(args[3])));
        files.put("META-INF/LICENSE-Orbit.txt", Files.readAllBytes(Path.of(args[4])));
        Remapper remapper = new Remapper(Opcodes.ASM9) {
            public String map(String name) { return name.startsWith("org/objectweb/asm/") ? name.replace("org/objectweb/asm/", "org/orbitlauncher/connect/internal/asm/") : name; }
        };
        Files.createDirectories(output.toAbsolutePath().getParent());
        try (JarOutputStream jar = new JarOutputStream(Files.newOutputStream(output))) {
            for (var entry : files.entrySet()) {
                String name = entry.getKey(); byte[] bytes = entry.getValue();
                if (name.endsWith(".class")) {
                    ClassReader reader = new ClassReader(bytes); ClassWriter writer = new ClassWriter(0);
                    reader.accept(new ClassRemapper(writer, remapper), 0); bytes = writer.toByteArray();
                    name = remapper.map(name.substring(0, name.length() - 6)) + ".class";
                }
                JarEntry item = new JarEntry(name); item.setTime(0); jar.putNextEntry(item); jar.write(bytes); jar.closeEntry();
            }
        }
        System.out.println("Built " + output + " (LAN agent, relocated ASM)");
    }
}

