using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.EditMenu;
using System.Text;

namespace DrSoft.MarkCard.Impl.Storage
{
    public static class MarkingParamHelper
    {
        private const int ParamsPayloadVersion = 1;
        private const string MarkCardParametersExtensionKey = "DrSoft.MarkCard.Parameters";

        /// <summary>
        /// params payload 是位置型二进制协议，内部结构为：
        /// 1. 去重后的 profile 表
        /// 2. entityId -> profileId 绑定表
        /// 当前版本只持久化 Engraving/Delay。
        /// </summary>
        public static Dictionary<string, byte[]> SerializeParams(
            IReadOnlyDictionary<int, IList<ParameterBase>> parameterPairs)
        {
            var defaultProfile = new StoredMarkingProfile(new EngravingParameter(), new DelayParameter(), new ExtendHeadTailSettingsModel(), new SkyWritingSettingsModel());
            var profiles = new List<StoredMarkingProfile>();
            var profileIds = new Dictionary<StoredMarkingProfile, int>();
            var bindings = new List<(int EntityId, int ProfileId)>();

            foreach (var pair in parameterPairs.OrderBy(it => it.Key))
            {
                var profile = ExtractStoredMarkingProfile(pair.Value);
                if (profile == null || Equals(profile, defaultProfile))
                {
                    continue;
                }

                if (!profileIds.TryGetValue(profile, out var profileId))
                {
                    var storedProfile = CloneProfile(profile);
                    profileId = profiles.Count;
                    profiles.Add(storedProfile);
                    profileIds[storedProfile] = profileId;
                }

                bindings.Add((pair.Key, profileId));
            }

            if (bindings.Count == 0)
            {
                return new Dictionary<string, byte[]>();
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(ParamsPayloadVersion);
            writer.Write(profiles.Count);
            foreach (var profile in profiles)
            {
                WriteEngravingParameter(writer, profile.Engraving);
                WriteDelayParameter(writer, profile.Delay);
                WriteExtendHeadTailSettingsModel(writer, profile.ExtendHeadTail);
                WriteSkyWritingSettingsModel(writer, profile.SkyWriting);
            }

            writer.Write(bindings.Count);
            foreach (var binding in bindings)
            {
                writer.Write(binding.EntityId);
                writer.Write(binding.ProfileId);
            }

            writer.Flush();
            var bytes = stream.ToArray();
            return new Dictionary<string, byte[]> { [MarkCardParametersExtensionKey] = bytes };
        }

        public static Dictionary<int, IList<ParameterBase>> RestoreParameters(
            Dictionary<string, byte[]> extensionPayloads)
        {
            if (extensionPayloads != null
                && extensionPayloads.TryGetValue(MarkCardParametersExtensionKey, out var parametersPayload)
                && parametersPayload.Length > 0)
            {
                var restoredPairs = DeserializeParams(parametersPayload);
                if (restoredPairs.Count > 0)
                {
                    return restoredPairs;
                }
            }

            return new Dictionary<int, IList<ParameterBase>>();
        }

        private static Dictionary<int, IList<ParameterBase>> DeserializeParams(byte[] payload)
        {
            if (payload.Length == 0)
            {
                return new Dictionary<int, IList<ParameterBase>>();
            }

            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

            var version = reader.ReadInt32();
            if (version != ParamsPayloadVersion)
            {
                throw new InvalidDataException($"不支持的打标参数 payload 版本: {version}");
            }

            var profileCount = reader.ReadInt32();
            if (profileCount < 0)
            {
                throw new InvalidDataException("打标参数 profile 数量无效");
            }

            var profiles = new List<StoredMarkingProfile>(profileCount);
            for (var profileIndex = 0; profileIndex < profileCount; profileIndex++)
            {
                profiles.Add(new StoredMarkingProfile(
                    ReadEngravingParameter(reader),
                    ReadDelayParameter(reader),
                    ReadExtendHeadTailSettingsParameter(reader),
                    ReadSkyWritingSettingsParameter(reader)
                    ));
            }

            var bindingCount = reader.ReadInt32();
            if (bindingCount < 0)
            {
                throw new InvalidDataException("打标参数绑定数量无效");
            }

            var result = new Dictionary<int, IList<ParameterBase>>(bindingCount);
            for (var bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
            {
                var entityId = reader.ReadInt32();
                var profileId = reader.ReadInt32();
                if (profileId < 0 || profileId >= profiles.Count)
                {
                    throw new InvalidDataException($"无效的 profileId: {profileId}");
                }

                result[entityId] = new List<ParameterBase>
                {
                    profiles[profileId].Engraving with { },
                    profiles[profileId].Delay with { },
                    profiles[profileId].ExtendHeadTail with {},
                    profiles[profileId].SkyWriting with {}
                };
            }

            return result;
        }

        private static StoredMarkingProfile? ExtractStoredMarkingProfile(IList<ParameterBase> parameters)
        {
            EngravingParameter? engraving = null;
            DelayParameter? delay = null;
            ExtendHeadTailSettingsModel? extendHeadTail = null;
            SkyWritingSettingsModel? skyWriting = null;

            foreach (var parameter in parameters)
            {
                switch (parameter)
                {
                    case EngravingParameter engravingParameter:
                        engraving = engravingParameter;
                        break;
                    case DelayParameter delayParameter:
                        delay = delayParameter;
                        break;
                    case ExtendHeadTailSettingsModel extendHeadTailSettingsModel:
                        extendHeadTail = extendHeadTailSettingsModel;
                        break;
                    case SkyWritingSettingsModel skyWritingSettingsModel:
                        skyWriting = skyWritingSettingsModel;
                        break;
                }
            }

            if (engraving == null && delay == null && extendHeadTail == null && skyWriting == null)
            {
                return null;
            }

            return new StoredMarkingProfile(
                engraving is null ? new EngravingParameter() : engraving with { },
                delay is null ? new DelayParameter() : delay with { },
                extendHeadTail is null ? new ExtendHeadTailSettingsModel() : extendHeadTail with { },
                skyWriting is null ? new SkyWritingSettingsModel() : skyWriting with { });
        }

        private static StoredMarkingProfile CloneProfile(StoredMarkingProfile profile)
        {
            return new StoredMarkingProfile(
                profile.Engraving with { },
                profile.Delay with { },
                profile.ExtendHeadTail with { },
                profile.SkyWriting with { });
        }

        /// <summary>
        /// profile payload 是位置型二进制协议，不是按属性名序列化。
        /// 当前版本下，Write/Read 的字段顺序必须严格一致，不能插入、删除或重排。
        /// 如果 EngravingParameter 字段发生变化，需要同时修改 Write/Read 并升级 payload 版本。
        /// </summary>
        private static void WriteEngravingParameter(BinaryWriter writer, EngravingParameter parameter)
        {
            writer.Write(parameter.ProcessingIndex);
            writer.Write(parameter.IsOutline);
            writer.Write(parameter.IsFill);
            writer.Write(parameter.IsFillPriority);
            writer.Write(parameter.Speed);
            writer.Write(parameter.Power);
            writer.Write(parameter.Frequency);
            writer.Write(parameter.EngraveCount);
            writer.Write(parameter.DotEngraveTime);
            writer.Write(parameter.EndPointDotTime);
            writer.Write(parameter.SpeedModeIndex);
            writer.Write(parameter.PrecisionFactor);
            writer.Write(parameter.OverlapRatio);
            writer.Write(parameter.IsOverlapEnabled);
        }

        private static EngravingParameter ReadEngravingParameter(BinaryReader reader)
        {
            return new EngravingParameter
            {
                ProcessingIndex = reader.ReadInt32(),
                IsOutline = reader.ReadBoolean(),
                IsFill = reader.ReadBoolean(),
                IsFillPriority = reader.ReadBoolean(),
                Speed = reader.ReadDouble(),
                Power = reader.ReadDouble(),
                Frequency = reader.ReadDouble(),
                EngraveCount = reader.ReadInt32(),
                DotEngraveTime = reader.ReadDouble(),
                EndPointDotTime = reader.ReadDouble(),
                SpeedModeIndex = reader.ReadInt32(),
                PrecisionFactor = reader.ReadInt32(),
                OverlapRatio = reader.ReadDouble(),
                IsOverlapEnabled = reader.ReadBoolean()
            };
        }

        /// <summary>
        /// profile payload 是位置型二进制协议，不是按属性名序列化。
        /// 当前版本下，Write/Read 的字段顺序必须严格一致，不能插入、删除或重排。
        /// 如果 DelayParameter 字段发生变化，需要同时修改 Write/Read 并升级 payload 版本。
        /// </summary>
        private static void WriteDelayParameter(BinaryWriter writer, DelayParameter parameter)
        {
            writer.Write(parameter.StartDelay);
            writer.Write(parameter.CornerDelay);
            writer.Write(parameter.EndDelay);
            writer.Write(parameter.EngraveDelay);
            writer.Write(parameter.JumpSpeed);
            writer.Write(parameter.JumpDelay);
        }

        private static DelayParameter ReadDelayParameter(BinaryReader reader)
        {
            return new DelayParameter
            {
                StartDelay = reader.ReadDouble(),
                CornerDelay = reader.ReadDouble(),
                EndDelay = reader.ReadDouble(),
                EngraveDelay = reader.ReadDouble(),
                JumpSpeed = reader.ReadDouble(),
                JumpDelay = reader.ReadDouble()
            };
        }
        private static void WriteSkyWritingSettingsModel(BinaryWriter writer, SkyWritingSettingsModel skyWriting)
        {
            writer.Write(skyWriting.IsEnabled);
            writer.Write(skyWriting.SkyWritingModel);
            writer.Write(skyWriting.DelayTime);
            writer.Write(skyWriting.LaserOnDelay);
            writer.Write(skyWriting.RunInTime);
            writer.Write(skyWriting.RunOutTime);
            writer.Write(skyWriting.ExtremeAngle);
        }
        private static SkyWritingSettingsModel ReadSkyWritingSettingsParameter(BinaryReader reader)
        {
            return new SkyWritingSettingsModel
            {
                IsEnabled = reader.ReadBoolean(),
                SkyWritingModel = reader.ReadUInt32(),
                DelayTime = reader.ReadDouble(),
                LaserOnDelay = reader.ReadInt32(),
                RunInTime = reader.ReadInt32(),
                RunOutTime = reader.ReadInt32(),
                ExtremeAngle = reader.ReadSingle()
            };
        }

        private static void WriteExtendHeadTailSettingsModel(BinaryWriter writer, ExtendHeadTailSettingsModel extendHead)
        {
            writer.Write(extendHead.HeadExtendLength);
            writer.Write(extendHead.TailExtendLength);
        }
        
        private static ExtendHeadTailSettingsModel ReadExtendHeadTailSettingsParameter(BinaryReader reader)
        {
            return new ExtendHeadTailSettingsModel
            {
                HeadExtendLength = reader.ReadSingle(),
                TailExtendLength = reader.ReadSingle()
            };
        }
    }

    internal sealed record StoredMarkingProfile(
        EngravingParameter Engraving,
        DelayParameter Delay,
        ExtendHeadTailSettingsModel ExtendHeadTail,
        SkyWritingSettingsModel SkyWriting);
}