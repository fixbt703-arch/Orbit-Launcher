package net.minecraft.client.multiplayer;
public final class ServerData {
    public enum Type { OTHER, LAN }
    public final String ip;
    private Type type = Type.OTHER;
    public ServerData(String value) { ip = value; }
    public boolean isLan() { return type == Type.LAN; }
}
