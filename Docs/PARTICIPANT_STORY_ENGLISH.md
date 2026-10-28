# 参与者自写故事：英语词形与自动西语括注

参与者仍然只写英语，不需要输入西班牙语，也不需要把句子强行改成英文原形。LLM 故事 Prompt 不变。

## 使用流程

1. 正常写完整英文故事，点击 **Split Story Into Sentences**。
2. 系统按英文原词或已知词形推断对应词。句子同时匹配多个目标词时保持 Unassigned，由参与者确认。每个句子仍需有一个分配，每个目标词仍需有匹配的英文句子。
3. 下方 preview 显示学习版，如 `I forgot (olvidar) the key.`；上方输入框和拆出的原始英文句子不变。
4. 确认后继续家具分配及 VR Study。学习故事、分词路线段、朗读/字幕解析和新会话包使用同一份已加括注的文本。

每个目标词只在它已分配句子中的第一次实际匹配处加一次括注，保留 `forgot`、`children` 等原始英文形式、大小写、句子内容及句子顺序。重复 finalize/preview 不应叠加同一括注。用户重新 Split 或改分配后，会从英文原文重新生成 preview，而不是编辑已加括注的文本。

## 识别范围及限制

- 支持离线表中的规则动词变化和不规则变化，如 `walked/walking`、`carried`、`forgot/forgotten/forgetting`、`ran/running`。
- 支持表中的规则/不规则复数及比较级/最高级，如 `birds`、`children`、`people`、`better/best`。
- 按词边界匹配，不在 `cart` 中匹配 `art`；不靠模糊拼写或词干截断猜词。不将 `hungry` 当作 `hunger`，也不将 `helpful` 当作 `help`。
- 这不是完整的 NLP 语义分析：同形异义、词性歧义仍需人工确认；系统不会判定故事中目标词的语义是否用对。
- 未收录词形保持原词精确匹配，不猜罕见变化。多词释义如 `look after` 暂只做完整短语匹配；`to forget` 的英文不定式提示词 `to` 可省略。
- 英语输入中的西语词不是新识别渠道。`olvidar` 不会被当作英文 `forget` 使用。

## 文本、导出与兼容性

`StorySessionData.originalParticipantStory` 保存参与者输入的原文，包括原有空白/换行。`fullStory` 是按原句序生成的学习版，空白按现有 Split 结果规范化；`orderedItems[*].storySegment` 与 `MnemonicItemData.mnemonic` 为学习版路线段。既有按目标词分组的路线规则不变。

`storyTextFormatVersion` 为 `english-span-es-v1`。这两个附加字段会随 VR package 和实验 JSON 导出保留；旧数据缺少字段时仍可加载。旧会话包不会被偷偷改写；使用新功能须重新确认故事、导出会话包。Quest 使用新逻辑须构建包含新代码与 Resource 的 APK，本次不进行 Quest 实机测试。

## 离线数据与重建

使用 [AGID 格式/版权说明](https://wordlist.aspell.net/agid-readme/)及固定的[原始数据快照](https://raw.githubusercontent.com/chadsowald/agid-irony-parser/6814807058dee1b9694dac547f768cd7428474c2/AgidIronyParser.Tests/infl.txt)。只保留已知词性的小写词条，排除带疑问标记的词形以及 level >= 2 的罕见/推测形式；按 lemma 合并，压缩为 UTF-8 表。未复制第三方解析器代码。

- `Assets/Resources/EnglishInflections.bytes`：39,738 个 lemma 的派生表，压缩后 276,879 字节；首次使用时解压，之后只为用到的释义缓存匹配器。
- `Assets/Resources/EnglishInflectionsNotice.txt`：上游完整说明、版权及数据快照来源。
- `Tools/Build-EnglishInflections.ps1`：机械转换脚本，检查原始文件 SHA256 后再生成上述资源。

重建时将固定快照保存为 `.utmp/agid-source/infl.txt`，把官方说明页面保存为 `.utmp/agid-source/readme.html`，在项目目录运行 `./Tools/Build-EnglishInflections.ps1`。运行游戏不需要 Python、额外服务器或联网。

## 检查

非 Play 状态运行 Unity 菜单 **Tools > Memory Palace > Check Participant Story English**。检查规则/不规则词形、误匹配防护、只标注第一次出现、原文保留、自动分类/歧义、路线顺序、JSON 往返、旧格式加载、VR package 和朗读段解析。不进行网络请求、音频播放或 XR 初始化；此类工程检查不验证学习效果。

已在独立最小 Unity 项目中通过 **109 项**检查，完整 Runtime C# 编译通过（仅有原有未使用字段警告）。最终日志为忽略目录 `.utmp/participant-story-english-final.log`。同时核对了既有本地 Azure 混合语言 SSML 包装：它按西语目标词本身匹配，不依赖括注方向，无需为 `English (Spanish)` 修改该服务。未进行实时语音请求或 Quest 实机测试。
