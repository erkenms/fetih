namespace Comet.Game.Packets
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.States;
    using Comet.Network.Packets;
    using Comet.Network.Sockets;
    using Microsoft.EntityFrameworkCore;
    using static Comet.Game.Packets.MsgTalk;

    /// <remarks>Packet Type 1001</remarks>
    /// <summary>
    /// Message containing character creation details, such as the new character's name,
    /// body size, and profession. The character name should be verified, and may be
    /// rejected by the server if a character by that name already exists.
    /// </summary>
    public sealed class MsgRegister : MsgBase<Client>
    {
        // Packet Properties
        public string CharacterName { get; set; }
        public ushort Mesh { get; set; }
        public ushort Class { get; set; }
        public uint Token { get; set; }
        public string MacAddress { get; set; }

        // Registration constants
        private static readonly byte[] Hairstyles = new byte[] {
            10, 11, 13, 14, 15, 24, 30, 35, 37, 38, 39, 40
        };

        /// <summary>
        /// Decodes a byte packet into the packet structure defined by this message class. 
        /// Should be invoked to structure data from the client for processing. Decoding
        /// follows TQ Digital's byte ordering rules for an all-binary protocol.
        /// </summary>
        /// <param name="bytes">Bytes from the packet processor or client socket</param>
        /// <remarks>
        /// 5619: confirmed in VM (124 bytes: zero at 4, name at 24, body at 72, class at
        /// 74, the MsgConnect token at 76, MAC address text at 80).
        /// </remarks>
        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            reader.BaseStream.Seek(24, SeekOrigin.Begin);
            var name = reader.ReadBytes(16);
            int end = Array.IndexOf(name, (byte)0);

            // One character per byte, so CharacterRules sees bytes outside ASCII
            this.CharacterName = System.Text.Encoding.Latin1.GetString(
                name, 0, end < 0 ? name.Length : end);
            reader.BaseStream.Seek(72, SeekOrigin.Begin);
            this.Mesh = reader.ReadUInt16();
            this.Class = reader.ReadUInt16();
            this.Token = reader.ReadUInt32();
            this.MacAddress = reader.ReadString(40);
        }

        /// <summary>
        /// Process can be invoked by a packet after decode has been called to structure
        /// packet fields and properties. For the server implementations, this is called
        /// in the packet handler after the message has been dequeued from the server's
        /// <see cref="PacketProcessor"/>.
        /// </summary>
        /// <param name="client">Client requesting packet processing</param>
        public override async Task ProcessAsync(Client client)
        {
            // Validate that the player has access to character creation
            // A newer login for the same account replaces this connection; only the
            // connection that currently holds the account may create its character.
            if (client.Creation == null || this.Token != client.Creation.Token ||
                !Kernel.Registration.ContainsKey(client.Creation.Token) ||
                !Kernel.Clients.TryGet(client.AccountID, out var current) || current != client)
            {
                LogEvents.For<MsgRegister>(LogEvents.CharacterCreateFailed).Warning(
                    "Character creation rejected for connection {ConnId} from {IP}: {Reason}",
                    client.ConnId, client.IPAddress, "invalid_token");
                await client.SendAsync(MsgTalk.RegisterInvalid);
                client.Disconnect(DisconnectReasons.AuthFailed);
                return;
            }

            // Check the name's characters, then whether it's free
            if (CharacterRules.CheckName(this.CharacterName) is string nameError)
            {
                LogEvents.For<MsgRegister>(LogEvents.CharacterCreateFailed).Information(
                    "Character creation for account {AccountId} failed: {Reason} ({Name})",
                    client.Creation.AccountID, nameError, this.CharacterName);
                await client.SendAsync(MsgTalk.RegisterInvalidName);
                return;
            }

            if (await CharactersRepository.ExistsAsync(this.CharacterName))
            {
                LogEvents.For<MsgRegister>(LogEvents.CharacterCreateFailed).Information(
                    "Character creation for account {AccountId} failed: {Reason} ({Name})",
                    client.Creation.AccountID, "name_taken", this.CharacterName);
                await client.SendAsync(MsgTalk.RegisterNameTaken);
                return;
            }

            // Validate character creation input
            if (!Enum.IsDefined(typeof(BodyType), this.Mesh) ||
                !Enum.IsDefined(typeof(BaseClassType), this.Class))
            {
                LogEvents.For<MsgRegister>(LogEvents.CharacterCreateFailed).Warning(
                    "Character creation for account {AccountId} failed: {Reason} " +
                    "(mesh {Mesh}, class {Class})",
                    client.Creation.AccountID, "invalid_input", this.Mesh, this.Class);
                await client.SendAsync(MsgTalk.RegisterInvalid);
                return;
            }

            // Create the character
            var character = new DbCharacter(client.Creation.AccountID, this.CharacterName);
            character.CurrentClass = (byte)this.Class;
            character.Mesh = this.Mesh;
            character.Silver = 1000;
            character.Level = 1;
            character.MapID = 1010;
            character.X = 61;
            character.Y = 109;
            character.Strength = 4;
            character.Agility = 6;
            character.Vitality = 12;
            character.Spirit = 0;
            character.HealthPoints = 
                (ushort)((character.Strength * 3) 
                + (character.Agility * 3)
                + (character.Spirit * 3)
                + (character.Vitality * 24));
            character.ManaPoints = (ushort)(character.Spirit * 5);
            character.Registered = DateTime.UtcNow;

            // Generate a random look for the character
            character.Avatar = (ushort)(character.Mesh < 1005 
                ? await Kernel.NextAsync(1, 49) 
                : await Kernel.NextAsync(201, 249));
            character.Hairstyle = (ushort)(
                (await Kernel.NextAsync(3, 9) * 100) + MsgRegister.Hairstyles[
                 await Kernel.NextAsync(0, MsgRegister.Hairstyles.Length)]);

            try 
            { 
                // Save the character and continue with login
                await CharactersRepository.CreateAsync(character); 
                Kernel.Registration.TryRemove(client.Creation.Token, out _);
                LogEvents.For<MsgRegister>(LogEvents.CharacterCreated).Information(
                    "Character {Name} created (account {AccountId}, character {CharacterId}, " +
                    "class {Class}, mesh {Mesh})",
                    character.Name, character.AccountID, character.CharacterID,
                    this.Class, this.Mesh);
                await client.SendAsync(MsgTalk.RegisterOk);
            }
            catch (DbUpdateException e) when (CharactersRepository.IsDuplicateName(e))
            {
                // Another player took the name since the check above
                LogEvents.For<MsgRegister>(LogEvents.CharacterCreateFailed).Information(
                    "Character creation for account {AccountId} failed: {Reason} ({Name})",
                    client.Creation.AccountID, "name_taken", this.CharacterName);
                await client.SendAsync(MsgTalk.RegisterNameTaken);
            }
            catch (Exception e)
            { 
                LogEvents.For<MsgRegister>(LogEvents.CharacterCreateFailed).Error(e,
                    "Character creation for account {AccountId} failed: {Reason} ({Name})",
                    client.Creation.AccountID, "db_error", this.CharacterName);
                await client.SendAsync(MsgTalk.RegisterTryAgain); 
            }
        }
    }
}