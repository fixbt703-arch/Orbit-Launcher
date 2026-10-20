package net.minecraft.client.multiplayer.resolver;
public final class ServerAddress {
    public final String address;
    private ServerAddress(String value) { address = value; }
    public static ServerAddress parseString(String value) { return new ServerAddress(value); }
}
