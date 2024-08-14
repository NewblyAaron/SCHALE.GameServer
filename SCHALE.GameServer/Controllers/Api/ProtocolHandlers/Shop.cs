using Microsoft.EntityFrameworkCore;
using SCHALE.Common.Database;
using SCHALE.Common.Database.ModelExtensions;
using SCHALE.Common.NetworkProtocol;
using SCHALE.Common.Utils;
using SCHALE.GameServer.Services;
using SCHALE.GameServer.Utils;
using Serilog;

namespace SCHALE.GameServer.Controllers.Api.ProtocolHandlers
{
    public class Shop : ProtocolHandlerBase
    {
        private const double DEFAULT_SSR_RATE = 1.5000;   // 0.0283% from default banner
        private const double DEFAULT_SR_RATE = 4.2500;   // 0.8409% from default banner
        private const double TENPULL_SR_RATE = 9.0000;   // 4.4091% from default banner
        private const double DEFAULT_R_RATE = 12.500;    //  7.134% from default banner
        private readonly Dictionary<Common.FlatData.CharacterExcelT, double> _pool = [];
        private readonly Dictionary<Common.FlatData.CharacterExcelT, double> _tenPullPool = [];

        private readonly ISessionKeyService _sessionKeyService;
        private readonly SCHALEContext _context;
        private readonly SharedDataCacheService _sharedData;
        private readonly ILogger<Shop> _logger;

        // TODO: temp storage until gacha management
        public List<long> SavedGachaResults { get; set; } = [];

        public Shop(IProtocolHandlerFactory protocolHandlerFactory, ISessionKeyService sessionKeyService, SCHALEContext context, SharedDataCacheService sharedData, ILogger<Shop> logger) : base(protocolHandlerFactory)
        {
            _sessionKeyService = sessionKeyService;
            _context = context;
            _sharedData = sharedData;
            _logger = logger;

            foreach (var chara in _sharedData.CharaList)
            {
                if (chara.Rarity == Common.FlatData.Rarity.SSR)
                {
                    _pool.Add(chara, DEFAULT_SSR_RATE);
                    _tenPullPool.Add(chara, DEFAULT_SSR_RATE);
                }
                else if (chara.Rarity == Common.FlatData.Rarity.SR)
                {
                    _pool.Add(chara, DEFAULT_SR_RATE);
                    _tenPullPool.Add(chara, TENPULL_SR_RATE);
                }
                else
                {
                    _pool.Add(chara, DEFAULT_R_RATE);
                }
            }
        }

        [ProtocolHandler(Protocol.Shop_BeforehandGachaGet)]
        public ResponsePacket BeforehandGachaGetHandler(ShopBeforehandGachaGetRequest req)
        {
            return new ShopBeforehandGachaGetResponse();
        }

        [ProtocolHandler(Protocol.Shop_BeforehandGachaRun)]
        public ResponsePacket BeforehandGachaRunHandler(ShopBeforehandGachaRunRequest req)
        {
            SavedGachaResults = [16003, 16003, 16003, 16003, 16003, 16003, 16003, 16003, 16003, 16003];

            return new ShopBeforehandGachaRunResponse()
            {
                SelectGachaSnapshot = new BeforehandGachaSnapshotDB()
                {
                    ShopUniqueId = 3,
                    GoodsId = 1,
                    LastResults = SavedGachaResults
                }
            };
        }

        [ProtocolHandler(Protocol.Shop_BeforehandGachaSave)]
        public ResponsePacket BeforehandGachaPickHandler(ShopBeforehandGachaSaveRequest req)
        {
            return new ShopBeforehandGachaSaveResponse()
            {
                SelectGachaSnapshot = new BeforehandGachaSnapshotDB()
                {
                    ShopUniqueId = 3,
                    GoodsId = 1,
                    LastResults = SavedGachaResults
                }
            };
        }

        [ProtocolHandler(Protocol.Shop_BeforehandGachaPick)]
        public ResponsePacket BeforehandGachaPickHandler(ShopBeforehandGachaPickRequest req)
        {
            var account = _sessionKeyService.GetAccount(req.SessionKey);
            var GachaResults = new List<GachaResult>();

            foreach (var charId in SavedGachaResults)
            {
                GachaResults.Add(new GachaResult(charId) // hardcode until table
                {
                    Character = new()
                    {
                        ServerId = account.ServerId,
                        UniqueId = charId,
                        StarGrade = 3,
                        Level = 1,
                        FavorRank = 1,
                        PublicSkillLevel = 1,
                        ExSkillLevel = 1,
                        PassiveSkillLevel = 1,
                        ExtraPassiveSkillLevel = 1,
                        LeaderSkillLevel = 1,
                        IsNew = true,
                        IsLocked = true
                    }
                });
            }


            return new ShopBeforehandGachaPickResponse()
            {
                GachaResults = GachaResults
            };
        }

        [ProtocolHandler(Protocol.Shop_BuyGacha3)]
        public ResponsePacket ShopBuyGacha3ResponseHandler(ShopBuyGacha3Request req)
        {
            var account = _sessionKeyService.GetAccount(req.SessionKey);
            var accountChSet = account.Characters.Select(x => x.UniqueId).ToHashSet();

            // TODO: Implement FES Gacha
            // TODO: Check Gacha currency
            // TODO: SR pickup
            // TODO: pickup stone count
            // TODO: even more...
            // Type          Rate  Acc.R
            // -------------------------
            // Current SSR   0.7%   0.7% 
            // Other SSR     2.3%   3.0%ServerId
            // SR           18.5%  21.5%
            // R            78.5%  100.%

            // const int gpStoneID = 90070086;
            const int chUniStoneID = 23;
            // var rateUpChId = 10095; // 10094, 10095
            // var rateUpIsNormalStudent = false;
            // bool shouldDoGuaranteedSR = true;
            // itemDict[gpStoneID] = 10;

            var gachaList = new List<GachaResult>(10);
            var itemDict = new AccDict<long>();
            // bool rigGacha = false;

            for (int i = 0; i < 10; ++i)
            {
                long chId = GachaProbability.Random(i == 9 ? _tenPullPool : _pool);
                bool isNew = accountChSet.Add(chId);

                gachaList.Add(new(chId)
                {
                    Character = !isNew ? null : new()
                    {
                        AccountServerId = account.ServerId,
                        UniqueId = chId,
                        StarGrade = 3,
                    },
                    Stone = isNew ? null : new()
                    {
                        UniqueId = chUniStoneID,
                        StackCount = 50,
                    }
                });
                if (!isNew)
                {
                    itemDict[chUniStoneID] += 50;
                    itemDict[chId] += 30;
                }
            }

            var executionStrategy = _context.Database.CreateExecutionStrategy();
            executionStrategy.Execute(() =>
            {
                using var transaction = _context.Database.BeginTransaction();

                try
                {
                    // add characters
                    _context.Characters.AddRange(
                        gachaList.Where(x => x.Character != null)
                                .Select(x => x.Character)!);

                    // create if item does not exist
                    foreach (var id in itemDict.Keys)
                    {
                        var itemExists = _context.Items
                            .Any(x => x.AccountServerId == account.ServerId && x.UniqueId == id);
                        if (!itemExists)
                        {
                            _context.Items.Add(new ItemDB()
                            {
                                IsNew = true,
                                UniqueId = id,
                                StackCount = 0,
                                AccountServerId = account.ServerId,
                            });
                        }
                    }
                    _context.SaveChanges();

                    // perform item count update
                    foreach (var (id, count) in itemDict)
                    {
                        _context.Items
                            .Where(x => x.AccountServerId == account.ServerId && x.UniqueId == id)
                            .ExecuteUpdate(setters => setters.SetProperty(
                                item => item.StackCount, item => item.StackCount + count));
                    }

                    _context.SaveChanges();

                    transaction.Commit();

                    _context.Entry(account).Collection(x => x.Items).Reload();
                }
                catch (Exception ex)
                {
                    _logger.LogError("Transaction failed: {Message}", ex.Message);
                    throw;
                }
            });

            var itemDbList = itemDict.Keys
                .Select(id => _context.Items.AsNoTracking().First(x => x.AccountServerId == account.ServerId && x.UniqueId == id))
                .ToList();
            foreach (var gacha in gachaList)
            {
                if (gacha.Stone != null)
                {
                    gacha.Stone.ServerId = itemDbList.First(x => x.UniqueId == gacha.Stone.UniqueId).ServerId;
                }
            }

            return new ShopBuyGacha3Response()
            {
                GachaResults = gachaList,
                UpdateTime = DateTime.UtcNow,
                GemBonusRemain = int.MaxValue,
                ConsumedItems = [],
                AcquiredItems = itemDbList,
                MissionProgressDBs = [],
            };
        }

        [ProtocolHandler(Protocol.Shop_List)]
        public ResponsePacket ListHandler(ShopListRequest req)
        {
            return new ShopListResponse()
            {
                ShopInfos = []
            };
        }

    }
}