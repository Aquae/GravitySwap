using log4net;
using Microsoft.Xna.Framework;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace GravitySwap
{
    public class AlignedPlayer : ModPlayer
    {
        Axiom config = ModContent.GetInstance<Axiom>();
        public ILog Logger = ModContent.GetInstance<GravityLink>().Logger;

        private int _partnerID = -1;
        public int PartnerID
        {
            get { return _partnerID; }
            set { _partnerID = value; }
        }
        public bool IsFlipped = false;
        public bool IsEntangled => PartnerID != -1 && (Main.player[PartnerID]?.active ?? false);
        private bool JustPressedUp = false;
        private bool canSwapGravity = false;

        public override async void OnEnterWorld()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                Sync();
                EmitRizz();
                
                await Task.Delay(10);
                Terraria.Chat.ChatHelper.SendChatMessageToClient(
                    NetworkText.FromLiteral($"[c/{config.NoticeColor}:Your mass is undergoing quantum alignment...]"),
                    Color.White,
                    Player.whoAmI
                    );
            }
            else if (Main.netMode == NetmodeID.SinglePlayer)
            {
                await Task.Delay(10);
                Terraria.Chat.ChatHelper.SendChatMessageToClient(
                    NetworkText.FromLiteral($"[c/{config.NoticeColor}:There is no reference plane to align your mass...]"),
                    Color.White,
                    Player.whoAmI
                    );
            }
        }

        public override void PlayerDisconnect()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                AlignedPlayer widow = Main.LocalPlayer.GetModPlayer<AlignedPlayer>();
                if (Player.whoAmI == widow.PartnerID)
                {
                    widow.Decoherence();
                }
            }
        }

        public override void OnRespawn()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient) { UpdateGravity(); }
        }

        public override void ProcessTriggers(TriggersSet triggersSet)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                if (canSwapGravity && IsEntangled && triggersSet.Up && !JustPressedUp && (Player.gravControl || Player.gravControl2) && !Player.pulley)
                {
                    Player.gravDir = IsFlipped ? -1f : 1f;
                    FlipGravity();
                }

                JustPressedUp = triggersSet.Up;
            }
        }

        public override void PostHurt(Player.HurtInfo info)
        {
            if (canSwapGravity && config.PainFlip && Main.netMode == NetmodeID.MultiplayerClient) { FlipGravity(); }
        }

        public override void PreUpdateMovement()
        {
            if (canSwapGravity && config.GravityJump && Player.justJumped && Player.whoAmI == Main.myPlayer && Main.netMode == NetmodeID.MultiplayerClient)
            {
                FlipGravity();
            }
        }

        private void FlipGravity()
        {
            if (IsEntangled)
            {
                IsFlipped = !IsFlipped;
                UpdateGravity();
            }
        }

        private void UpdateGravity()
        {
            if (IsEntangled)
            {
                Player partner = Main.player[PartnerID];

                Player.forcedGravity = IsFlipped ? int.MaxValue : 0; 
                partner.forcedGravity = !IsFlipped ? int.MaxValue : 0;

                Player.gravDir = IsFlipped ? -1f : 1f;
                partner.gravDir = IsFlipped ? -1f : 1f;
                
                ModPacket packet = Mod.GetPacket();
                packet.Write((byte) MessageType.Flux);
                packet.Write(IsFlipped);
                packet.Send();
            }
        }

        private void Sync()
        {
            ModPacket packet = Mod.GetPacket();
            packet.Write((byte) MessageType.SyncRequest);
            packet.Send();
        }

        private void EmitRizz()
        {
            ModPacket packet = Mod.GetPacket();
            packet.Write((byte) MessageType.Rizz);
            packet.Send();

        }
        
        public async void Entangle(int partnerID, bool isFlipped) {
            int secondsDelay = config.GravityDelaySeconds;
            PartnerID = partnerID;
            Logger.Info($"{Player.name} has entangled with {Main.player[partnerID].name}");
            Main.NewText($"[c/{config.NoticeColor}:Your mass is now quantum entangled with ][c/{config.PlayerColor}:{Main.player[partnerID].name}]");
            Main.NewText($"[c/{config.WarningColor}: Prepare for gravitational desynchronisation in {secondsDelay}...]");
            
            IsFlipped = isFlipped;
            canSwapGravity = false;

            if (secondsDelay > 3) {
                await Task.Delay((secondsDelay - 3) * 1000);
            }

            int countdownStart = secondsDelay > 3 ? 3 : secondsDelay;
            for (int t = countdownStart; t >= 1; t--) {
                if (!IsEntangled) return;
                Main.NewText($"[c/{config.WarningColor}: {t}...]");
                await Task.Delay(1000);
            }

            canSwapGravity = IsEntangled;
            if (IsFlipped) { UpdateGravity(); }
        }

        public void Decoherence()
        {
            canSwapGravity = false;
            Logger.Info($"{Player.name} is experiencing decoherence.");
            
            ModPacket packet = Mod.GetPacket();
            packet.Write((byte) MessageType.Decoherence);
            packet.Send();

            PartnerID = -1;
            Player.forcedGravity = 0;
            Terraria.Chat.ChatHelper.SendChatMessageToClient(
                NetworkText.FromLiteral($"[c/{config.NoticeColor}:Quantum decoherence has occurred. You are no longer entangled with your partner.]"),
                Color.White,
                Player.whoAmI
            );
        }
    }

    public class GravityLink : Mod
        {
            private void HandleDecoherence(BinaryReader reader, int whoAmI)
            {
                switch (Main.netMode)
                {
                    case NetmodeID.Server:
                        AlignedPlayer widow = Main.player[whoAmI].GetModPlayer<AlignedPlayer>();
                        widow.PartnerID = -1;
                        widow.Player.forcedGravity = 0;

                        ModPacket packet = GetPacket();
                        packet.Write((byte) MessageType.Decoherence);
                        packet.Write((byte) whoAmI);
                        packet.Send(-1, whoAmI);
                        break;
                    
                    case NetmodeID.MultiplayerClient:
                        int playerID = reader.ReadByte();
                        Player player = Main.player[playerID];

                        player.forcedGravity = 0;
                        break;
                }
                        
            }

            private void HandleFlux(BinaryReader reader, int whoAmI)
            {
                AlignedPlayer player;
                AlignedPlayer partner;
                bool isFlipped;

                switch (Main.netMode)
                {
                    case NetmodeID.Server:
                        isFlipped = reader.ReadBoolean();
                        player = Main.player[whoAmI].GetModPlayer<AlignedPlayer>();
                        partner = Main.player[player.PartnerID].GetModPlayer<AlignedPlayer>();

                        partner.IsFlipped = player.IsFlipped;
                        player.IsFlipped = !player.IsFlipped;
                        
                        partner.Player.forcedGravity = partner.IsFlipped ? int.MaxValue : 0;
                        player.Player.forcedGravity = player.IsFlipped ? int.MaxValue : 0;

                        partner.Player.gravDir = partner.IsFlipped ? -1f : 1f;
                        player.Player.gravDir = player.IsFlipped ? -1f : 1f;

                        ModPacket packet = GetPacket();
                        packet.Write((byte) MessageType.Flux);
                        packet.Write((byte) whoAmI);
                        packet.Write((byte) player.PartnerID);
                        packet.Write(isFlipped);
                        packet.Send(-1);
                        break;
                    
                    case NetmodeID.MultiplayerClient:
                        int playerID = reader.ReadByte();
                        int partnerID = reader.ReadByte();
                        isFlipped = reader.ReadBoolean();

                        Main.player[playerID].forcedGravity = isFlipped ? int.MaxValue : 0;
                        Main.player[partnerID].forcedGravity = !isFlipped ? int.MaxValue : 0;

                        Main.player[playerID].gravDir = isFlipped ? -1f : 1f;
                        Main.player[partnerID].gravDir = !isFlipped ? -1f : 1f;

                        if (partnerID == Main.LocalPlayer.whoAmI)
                        {
                            partner = Main.LocalPlayer.GetModPlayer<AlignedPlayer>();
                            partner.IsFlipped = !isFlipped;
                        }
                        break;
                }
            }

            private void ForwardRizz(int whoAmI)
            {
                AlignedPlayer rizzler = Main.player[whoAmI].GetModPlayer<AlignedPlayer>();
                AlignedPlayer rizzed;
                
                foreach (Player player in Main.ActivePlayers)
                {
                    rizzed = player.GetModPlayer<AlignedPlayer>();

                    if (rizzler == rizzed) { continue; }

                    if (!rizzed.IsEntangled) {
                        rizzed.PartnerID = whoAmI;
                        rizzler.PartnerID = rizzed.Player.whoAmI;

                        bool randomPolarity = new Random().Next(2) == 0;
                        
                        ModPacket rizzedPacket = GetPacket();
                        rizzedPacket.Write((byte) MessageType.Rizz);
                        rizzedPacket.Write((byte) whoAmI);
                        rizzedPacket.Write(randomPolarity);
                        rizzedPacket.Send(rizzed.Player.whoAmI);

                        ModPacket rizzlerPacket = GetPacket();
                        rizzlerPacket.Write((byte) MessageType.Rizz);
                        rizzlerPacket.Write((byte) rizzed.Player.whoAmI);
                        rizzlerPacket.Write(!randomPolarity);
                        rizzlerPacket.Send(whoAmI);
                    }
                }
            }

            private void HandleRizz(BinaryReader reader, int whoAmI)
            {
                switch (Main.netMode)
                {
                    case NetmodeID.Server:
                        ForwardRizz(whoAmI);
                        break;
                    case NetmodeID.MultiplayerClient:
                        int partnerID = reader.ReadByte();
                        bool isFlipped = reader.ReadBoolean();
                        AlignedPlayer rizzed = Main.LocalPlayer.GetModPlayer<AlignedPlayer>();
                        rizzed.Entangle(partnerID, isFlipped);
                        break;
                }  
            }

            private void HandleSyncRequest(BinaryReader reader, int whoAmI)
            {
                ModPacket packet;
                switch (Main.netMode)
                {
                    case NetmodeID.Server:
                        packet = GetPacket();
                        packet.Write((byte) MessageType.SyncRequest);
                        packet.Write((byte) whoAmI);
                        packet.Send(-1, whoAmI);
                        // I decided it was too much effort to send a list of some sort containing flip states so I'll just have seperate packets from the clients
                        // But yeah better option is for the server to respond with the data and not bother the other clients
                        break;
                    
                    case NetmodeID.MultiplayerClient:
                        packet = GetPacket();
                        packet.Write((byte) MessageType.SyncResponse);
                        packet.Write(reader.ReadByte()); // Player requesting sync
                        packet.Write((byte) Main.myPlayer);
                        packet.Write(Main.LocalPlayer.forcedGravity > 0);
                        packet.Send();
                        break;
                }
            }

            private void HandleSyncResponse(BinaryReader reader, int whoAmI)
            {
                int playerID;
                int responderID;
                bool isFlipped;

                switch (Main.netMode)
                {
                    case NetmodeID.Server:
                        playerID = reader.ReadByte();
                        responderID = reader.ReadByte();
                        isFlipped = reader.ReadBoolean();
                        
                        ModPacket packet = GetPacket();
                        packet.Write((byte) MessageType.SyncResponse);
                        packet.Write((byte) responderID);
                        packet.Write(isFlipped);
                        packet.Send(playerID);
                        break;
                    
                    case NetmodeID.MultiplayerClient:
                        responderID = reader.ReadByte();
                        isFlipped = reader.ReadBoolean();
                        
                        Player player = Main.player[responderID];
                        player.forcedGravity = isFlipped ? int.MaxValue : 0;
                        player.gravDir = isFlipped ? -1f : 1f;
                        break;
                }
            }

            private void HandleConfigRequest(BinaryReader reader, int whoAmI)
            {
                if (Main.netMode == NetmodeID.Server)
                {
                    Axiom config = ModContent.GetInstance<Axiom>();
                    ModPacket packet = GetPacket();
                    packet.Write((byte)MessageType.ConfigSync);
                    packet.Write(config.GravityDelaySeconds);
                    packet.Write(config.PainFlip);
                    packet.Write(config.GravityJump);
                    packet.Send(toClient: whoAmI);
                }
            }

            private void HandleConfigSync(BinaryReader reader, int whoAmI)
            {
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    Axiom config = ModContent.GetInstance<Axiom>();
                    config.GravityDelaySeconds = reader.ReadInt32();
                    config.PainFlip = reader.ReadBoolean();
                    config.GravityJump = reader.ReadBoolean();
                }
            }

            public override void HandlePacket(BinaryReader reader, int whoAmI)
            {
                MessageType msgType = (MessageType) reader.ReadByte();
                
                switch (msgType)
                {
                    case MessageType.Decoherence:
                        HandleDecoherence(reader, whoAmI);
                        break;

                    case MessageType.Flux:
                        HandleFlux(reader, whoAmI);
                        break;

                    case MessageType.Rizz:
                        HandleRizz(reader, whoAmI);
                        break;
                    
                    case MessageType.SyncRequest:
                        HandleSyncRequest(reader, whoAmI);
                        break;
                    
                    case MessageType.SyncResponse:
                        HandleSyncResponse(reader, whoAmI);
                        break;
                        
                    case MessageType.ConfigSync:
                        HandleConfigSync(reader, whoAmI);
                        break;
                }
            }
    }

public class Axiom : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ServerSide;

        [Header("Triggers")]
 
        [DefaultValue(false)]
        public bool PainFlip { get; set; }

        [DefaultValue(false)]
        public bool GravityJump { get; set; }

        [Header("Timing")]
        [DefaultValue(5)]
        [Tooltip("Delay in seconds before gravity swaps after entanglement")]
        public int GravityDelaySeconds { get; set; }

        [Header("Theme")]

        [DefaultValue("00FFD1")]
        public string NoticeColor { get; set; }

        [DefaultValue("FF0F0F")]
        public string WarningColor { get; set; }

        [DefaultValue("5200FF")]
        public string PlayerColor { get; set; }
    }

    internal enum MessageType : byte
    {
        Decoherence,
        Flux,
        Rizz,
        SyncRequest,
        SyncResponse,
        ConfigRequest,
        ConfigSync
    }
}