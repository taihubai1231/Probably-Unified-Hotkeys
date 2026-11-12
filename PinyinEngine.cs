#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 高性能纯内存拼音检索引擎 (全覆盖 CJK Unicode 0x4E00..0x9FA5 共 20902 字)
    /// </summary>
    public static class PinyinEngine
    {
        private const int CJK_START = 0x4E00;
        private const int CJK_END = 0x9FA5;
        private const int CJK_COUNT = CJK_END - CJK_START + 1;

        private static readonly string InitialsTable = "ydkqsxhwzssxjbymgcczqpssqbycdscdqldylybsgjgyqzjjfgcclzzbwdwzjljpfyynwjjtmyyzwzhflyppqhgccyyymjqyxxgjxhsdsjnjjsmhmlzrxyfsngsyczqzggllyjlmyzssecykyyhqwjssggyxyqyjtwkdjhychmyxjtlxjyqbyxdldwrrjjwysrldzjpcbzjjbrcfslbczstzfxxthtrqggbdlyccscymmrfcyqzpwwjjyfcrwfdfzqpyddwyxkyjawjffxjpdftzyhhycyswccyqsclcxxwzzxnbgnnxbxlzsqcbsgpysyzdhmdzbqbzcwdzzyytzhbtsyyfzgntnxqywqskbphhlxgybfmjebjhhgqtjcysxstkzglyckglysmzxyalmeldccxgzyrcxsdltjzcqkcnnjwhjczzcqljststbnxbtyxceqxgkwjyflzqlyhjqspsfxlfpbyqxxxydcczylllsjxfhjxpjbcffyabyxbhczbjyclwlczggbtssmdtjcxpthyqtgjjscjfzkjzjqnlzwlslhdzbwjncjzyzsqnycqyrzcjjwybrtwpyftwexcskdzctbxhyzcyyjxzcfbzzmjyxxcdczottbzljwfcgszsxfyrlnyjmbdthjxsqjccsbxyytsyfbjdztgbcnclcyzzbsacyzzscjcshzqydxlbpjllmqxtydzxsqjtzpxlcglqccwjbhctdjjsfxjejjtlbgxsxjmyjjqpfzasyjncydjxkjcdjszcbartcclnjqmwnqnclllkbybzzsyhqcltwlccrshllzntylnewyzyxczxxgdkdmtcedejtsyys dqdfmsd jlhrwnqlybglxhlgtgxbqjdzfyjsjyjcjmrnymgrcjczgjmzmgxmmryxkjnymsgmzjymklfxmbdtgfbhcjhkylpfmdxlqjjsmtqgzsjlqdldgjycalcmzcsdjllnxdjffffjczfmzffpfkhkgdpqxktacjdhhzdddrrcfqyjkqccwjdxhwjlyllzgcfcqdsmlzpbjjplsbcjggdckkdezsqsckjgcgkdjtjllzycxklqscgjcltfpcqczgwbjdqsdjjbyjhsjddwgfsjgdkccctllpspkjgqjhzzljplgjgjjthjjyjzcjmlzlyqbgjwmljkxzdznjqsyzmljlljkywxmkjlhskjgbmclyymkxjqlbmclkmdxxkwyxwslmlpsjqjcqxyjfjtjdxmxxllcrqbsyjbgwywxggbcyxpjtgpepfgdjgbhbnsfjyzjkjkhxqfgqzkfhygkhdgllsdjjxpqykybnqsxqnszswhbsxwhxwbzzxdmndjbsbkbbzklylxgwxjjwaqzmywsjqlcjxxjqwjeqxscwetlzhlyyysdzpyqyzcptlshtzcfycyxyljsdcjjagyslcllyyysglrqqeldxzsccccadycjysfsgbfrsszqsbxjpsjwsdrckgjlgdkzjzbdktcsyqpyhstcldjlhmxmcgxyzhjdctmhltxzxylymohyjcltyfbqqjbfbdfehtksqhzywwcnxxcdwhhwgyjlegmdqcwgfjhcsntwydolbygwqwesjpwnmlrydzsztxyqpzgcwxhngpyxshmdqjgztdppbfyhzhhjyfdzwkgkzbldntsxhqeegzxylzmmzyjzgszxkhkhtxexxgylyapsthxdwhzydpxagkydxbhnhxkdfjnmyhylpmgocslnzhkxxlbzzlbmlsfbhhgsgyyggbhscyajtxwlxtzqcwzydqdqmmgdqllszhlsjzwfjhqswscelqazynytlsxthaznkzzsdhlacxtwwcsgqqtddyzbcchyqzflxpslzygpzsznglydqcbdlxjtctajdkywnsyzljhhdzcwnyyzyomhychhhxhjkzwsxhdnxlyscqydpclyzwmypbkxyjlkzhtyhaxqsyshxasmchkdscrswjpwqsgzjlwwschs hsqnhzsngndaqtbaalzzmsstdqjcjktscjaxplggxhhgoxzcxpdmmhldgtybysjmxhmrcplxjzckzxshflqxccdhxezfchzccdytcjyxqhlxdhypjqxnlsyydzozjnyxqezysjyayjkypdghddxsppyzndlthrhxydpcjjhtcxmctlhbynyhmhzllhnxmylllmdcppxhmxdkycyrdltxjchhznxclcclylnzsxzjzzlnnllwhyqsnjhxynttdkyjpychhyegkcttwlgqrlggtgtygyhpyhylqyqgcwyqkpyyyttttlhyhlltyttsplkyzwgywgpydqqzzdqxskcqnmjjzzbxyqmjrtfbbtkhzkbjdjjkdjjtlbwfzpbtkqtztgpdgntpjyfalqmkgxbcclzfhzclllladpmxdjhlcclgyhdzfgyddgcyyfgydxkssebdhykdkdkhnaxxybfbyyhxcqgabfqyjjdmljcsjzllpchbsxgjyndybyqspqwjlzkcddtaccbkzdyzypjzqsjnkktknjdjgyepgtlfyqkasdntcyhblgdzhbbydmjrygkzyheyybcmcdtyfzjjhgcjplxhldwxjjkytcyksssmtwcttqzlzbszdtwzxgzagyktywxlhlcpbclloqmmzsslcmbjcszzkydczxgqjdsmcytzqqlwzqzxssbpkdfqmddzdsddtdmfhtdyzjaqjqkypbdjyyxtljhdrqxxxhaydhrjlklytwhllrllrcxylbwsrszzsymkzzhhkyhxksmzsyzgcjfbzbsqlfcxxxnxkxwymsddyqwggqmmyhcdzttfgyyhgstttybykjdhkyjbelhdypjqnfxfdqkzhqkzbyjtzbxhfdxbdaswhawajldyjsfhbldnndnqjtjnchxfjsrfwhzfmdrfjyhwzpdjkzyjymfcyznynxfbytfwfwygdbnzzzdnytxzemmqbsqehxfzmbmflzzsrsymjgsxwzjsprydjsjgxhjjgljjynzjjxhgjkymlpeyycsysgqzswhwlyrjlpxslcxmfsmwkcctnxnynpnjszhdzeptxmwywayysywlxjqzqxzdclaeelmcpjpclwbxsqhfwrtffjtnqjhjqdxhwlbyccfjlalkyyjldxhhycstdywncjtxywdrmdrqhwqcmfjdyzmhmayxjwmyzqsxtlmrspwwchajbxtgcypxyyrrclmpamgkqjszyjrmyjsnxtplnbappypylxmyzkynldgyjzczhnlmzhhanqmpgwqtzmxxmllhgdzxyhxkrxycjmffxyhjfsbssqlhxndycannmtcjcyprrnytyqnyymbmsxndlylysljnlqyshqmllyzlzjjjkymzcsfbzxxmstbjgnxyzhlsnmcqscyznfzlxbrnnnylmnrtgzqysatswryhyjzmzdhzgzdwybsscskxsyhytsxgcqgxzzbhyxjscrhmkkbsczjyjymkqhzjfnbhmqhysnjnzybknqmcjgqhwlsnzswxkhljhyybqcbfcdsxdldspfzfskjjzwzxsddxjseeegjscssmgclxxkywyllymwwwgydkzjgggtggsycknjwnjpcxbjjtqtjwdsspjxzxnzxwmelptfsxtllxcljxjjljsxctnswxlehhlyqrwhsycsqrybyaywjejqfwqcqqcjqgxaldbzzyjgkgxpltqyfxjltpadkyqhpmatlcpdhkxmtxybhblefxdleegqdymsawhzmljtwyqxlyjzljeeyxbqqffnlyxrdsctgjgxyylkllxqkcctlhjlqmkkzgcyygllljdzgydhzwxpysjbzkdzgyzzhywyfqytyzszyezklymhjjhtsmqwyzlkyywzcsrkqyqltdxwcdrjklwsqzwbdcqyncjsrszjlkcdcdtlzzzacqqczddxyplxcbqjylzllljddzjgyjyjzyxnyyynxjxkxdazwyrdljyyyrjlglldrxjcykywnqcclddnyyykyckczhjxcclgzqjgjwppcqqjysbzzxyjxjbxjfzbsbdsfnsfpzxhdwztdmpptblzzbzdmyypqjrsdzsqzsqxbdgcpzswdwcsqzgmdhzxmwwfybpdgphtmjthzsmmbgzmbzjcfzhfcbbzmqcfmbcmcjxlgpnjbbxgyhyyjgptzgzmqbqdcgybjxlwzkydpdymgcftpfxyztzxdzxtgkmtybbclbjaskytssqyymscxfjeglsllszbqjjjaklyldlycctsxmcwfgkkbqxlllljyxtyltyxytdpjhnhgnkbyqnfjyyzbyyessessgdyhfhwtcjbsdzjtfdmxhcnjzymqwsrxjdzjqpdqbbsdjggfbkjbxdgjhmgwjjjgdllthzhhyyyyyysxwtyyyccbdbpypzyccztjpzywcbdlfwzcwjdxxhyhlhwczxjtczlcdpxdjczczlyxjjsjbhfxwpywxzptdzzbdccjhjhmlxbqxxbylrddgjrrctttgqsczwmxfytmwzcwjwxjywcskybzqccttqnhxnkxxkhkfhtswoccjybcmpzzyjbnnzpbthhjdlscddytyfjpxyngfxbyqxcbhxcbsxtyzdmzysnxsxlhkmzxlthdhkghxjsshqyhhcjyxglhzxcsnhekdtgqxqypkdhextykcnymyyypkqyytjxzlthhqtbyqhxbmyhsqckwwyllhcyylnneqxqwmcfbdccmljggxdqktlxkgnqcdgzjwyjjlyhhqtttnwchhxcxwheszjydjccdbqcdgdnyxzdhcqrxcbmztqcbxwgqwyybxhmbymykdyecmqkyaqyngyzslfykkqgyssqyshjgjcnxkzycxsbkyxhyylstycxqthysmgscpmmgcccccmtztasmgqzjhklosqylswtmqsyqkdzljqqyplcycztcqqpbbqjzclpkhqcyyxxdtdddsjcxffllchqxmjlwcjcxtspycxndtjshjwxdqqjckxyamylsjhmlalykxcyydmamdqmlmcznnyybzkkyflmchcmlhxrcjjhsylnmtjggzgywjxsrxcwjgjqhqzdqjdzjjzkjkgdzqgjjyjylhzxxcdqhhhestmhlfsbdjsyyshfyssczqlpbdrfrztzdkykgsctgkwdqzrkmsynbcrxqbjyfaxpzzedzcjykbcjwhyjbqdzywnyszptdkzpfpbaztklqyhbbzpnbptyzzybhnydcpjmmcycqmcjfzzdcmnlfpbplngqjtbttajzpzbbdnjkljqylnbzqhksjznggqsczkyxchpzsnbcgzkddzqanzgjkdntlzldwjljzlywtxndjzjhxyatncbgtzcsskmnjpjytsrwxcfjwjjtkhtzplbhsnjzsyjbwbzyzlstlsbjhdwwqpslmmfbjdwajyzccjtbnnrzwqxcdslqgdsdpdzhjtqqpsqlyyjzlgyhszlctcbjtktyczjtqkbpjlgmjzdmcsgpynjzjjyyknhrpwszxmtncszzyxybyhyzaxywkcjtllckjjtjhgcxdxyqyczbywblwqcglzgjgqrqcczssbcrbcskydznljsqgxssjmecnstztpbdlthzwhqwqtzexnqczgweskssbybstscsjccgbfsdqszlccglllzghzcthcnmjgyzaznmckcstjmmzckbjygqljyjppldxrgzyxccsnhshgdznlzhzjjcddcbcjflbfqbczzwpqdnhxljcthqwjgylnlszzpcjdscqqhjqkdxkpbajyemsmjtzdxlcjyryynwjbngzzkmjxltbsllrtpylcsznxjhllhyllqqzqlxymrcwcxsljmczltzldwdjjllnzggqxppskygyggbfzpdkmwghcxmcgdxjmcjsdycabxjdlnbcddygskydjtxdjjyxmsaqazdzfslqxyjsjzylblxxwxqqzbjzlfbblylwdsljhxjyzjwtdjcyfqzqzzdcsxzzqlzcdzfchyspympqzmlpplffxjjnzzylsjyyqzfpfzksywjjjhrdjzzxtxxglghtdxcskyswmmtcwybazbjkshfhgcxmhfqhyxxyzftsjyzbxyxpzlchmzmbxhzzssyfdmncwdabazlxktcshhxkxjjzjsthygxsxyyhhhjwxkzxcsbzzwwhhcwtzzzpjxsnxqqjgzyzawllcwxzfxgyxyhxmkyyswsqmnjnaycyspmjkgwcqhylajjmzxhmmcnzhbhxclxtjpltxyjhdyylttxfszhyxxsjbjyayrsmxyplckdlyhlxrlnllstyzyyqygyhhsccsmcctzcxhyqfpyyrpfflfqtntszllzmhwtcjqyzwtllmlmdwmbzssmzrbpdddlgjjbxccsrzqqygwcsxfwzlxccrbtdzmcyggdlqsgtjmwljmymmsyhfbjdgyxccpshxczcsbsjwjgjmpbwaffyfnxhydxzylremzgzcyhsszdlljcsqfzxxkptxzgxjjgbmyyysnbdylbnlhbfzdcyfbmgqrrmsszxysgtznnydzzcdgbjafjbdknzblcsscpsgzycjszlmlrzzbzzldlsllysxsqzqlyxzlsgkbrxbrbzcycxzjzeeyfgklzlyyhgysgzlfjhgtgwkraajyzkzqtsshjjxdzyz yjlzyrzdqqhgjzxsszbtkjpbfrtjxllfqwjgslqtymblpzdxtzagbdhzzrbgjhwnjtjxlhscfsmwlldqysjtxkzscfwjlbxftzlljzllqblcqmqqcgcdfpbbhzczjlpyygjdtgwdcfczqyyyqysrclqzfklzzzgffsqnwglhjycjjczlqzcyjbjzzbpdccmhjgxdqdgdlzqmfgpsytsdyfwwdjzjysxyycjcyhzwpbyhxrylybhkjksfxtzjmmchhlltnyymsxxyzpyjjycdyzwmtjjkqyrhllqxpsgtlwycljscbxjyzfnmlrgjjtyzbsyzmsjyjhgfzqmsyxrszcwtlrtqzsstkxgqggsptgcdnjsgcqcqhmxggztqydjkzdlbzsxjlhyqgggthqscpyhjhhgnygkggcmjdzllcclxqsftgzslllmlcskctbljzzszmmnytpzsxqhjcjyqxyexzqzcpshkzzysxcdfgmwqrllqxrfztlysdctmjcsjjdhjnxtnrztzfqrhqgllgcxszsjdjljcytsjtlnyxhszxcgjzyqpylfhdjsbpcczgjjjqzjqdybssllcmyttmqtbhjqnnygkynqyqmzgcjkpdcgmyzhqllsllclmholzgdylfzsljcqzlylzcjeshnylljxgjxlyjyyyxnbcljsswcqqcjyllcldjyllzllbnylgqchxyyqoxccqkyjxxhyklksxayqccqkkkkcsgyxxyqxygwtjohthxpxxcsshcyeychzzcbwqbbwjqcscszsslzylgdesjzmmymcytsdsxxscjpqqsqylyfzychdjdzywcbtjsydjhcyddjlbdjjsodzyqysqkxxdhhgqjyohdyxwgmmmajdybbbppbcmhcpljzsmtxerxjmhqdstpjdcbssmsssthjtslmmtrcplzszmlqdsdmjmqpnqdxcfynbfsdqqyxhyaykqyddlqyyysszbydslntfgtzqbzmchdhczcwfdxtmqqsphqwwxsrgjcwtjtzzqmgwjjrjhtqjbbgwzfxjhnqfxxqywyyhyscdydhhqmnmdmmcpbszppzzglmzfollcfwhmmsjzttthlmyffytzzgzyskjjxqyjzqphmbzzlyghgfmshpcfzsnclpbqsnjszslxjfpmtyjygbxlldlxpzjypjyhhzcywhjylsjexfsszywxkzjlladtmlymqjpwxxhxsktqjezrpxxzghmhwqpwqlyjjqjjzszcfhjlchhnxjlqwzjhbmzyxbdhhypylhlhlgfwlcfyytlhjjcjmscpxstkpnhjxsntyxxtestjctlsslstdlllwwyhdhrjzsfgxssyczykwhtdhwjslhtzdqdjzxxqggyltzphcsqfzlnjtclzpfstpdynylgmjllycqhynsbchylhqyqtmzybbywrfqykjsyslzdyjmpxyyssrhzjnyqtqdfzbwwdwwrxcwhgyhxmkmyyyhmsmzhngcepmlqqmtcwctmhmxjpjjhfxyyzsjzhtybmstsyjdtjjqytlhynbyqzlcxcnzwsmylkfjxlwgbypjytysylymzckttwlgsmzsylmpwlzwxwqzssaqsyxyrhssntsrapccpwcmgdhhxzdzxfjhgzttsbjhgyglzysmyclllybtyxhbbzjkssdmalhhycfygmqypjycqxjllljgclzgqlycjcctotyxmtmshllwcgfxymzmklpszzzxhhjyslctyjcyhxsgyxzkxlzwpyjpdhjwpjpwsqqxlxxdhmrslzcyzwstcxkystzshbsccstplwsscjchjlcgchssphylhfhhxjsxyllnylmzdhzxylsxlwzyhcldyahzcmddyspjtqjzlngjfsjshctsdszlblmssmnyymjqbjhrcwtyydchqljapzwbgqybkfcmjwlzllyylszydwhxpsbcmljpscgbhxlqhyrljxyswxhxzlldfhlslymjljyflyjycdrjlfsyzfsllcqyqfgjyhyszlylmstdjcyhbzllnwlxxygyyhbmgdhxxhhlzzjzxczzzcyqzfnjwpylcpkpykpmclgkdgxzggwqbdxzzkzfbxxlzxjtpjpttbytszzdwslchzhsltjxhqlhyxxxywzyswtmzkhlxzxzpyhgchkjfsyh tjrlxfjxptztwhplyxfcrhxshxkjxxyhzjdxjwylhyhmjdbflkhtxcwhcfwjcfpqrxqxcyyyjygrpxgscsxngwchkzdxhflxxhjjbyzwtsxnncyjjymswzjqrmhxzwfqsylzjzgbhynslbgttcsebhxxwxyhhxyxnsqyxmlywrgyqlxbbcljsylpsytjzyhyzawlhorjmksczjxxxyxchcytryxqjddsjfslyltsffyxlmtyjmjjyyyxltzcsxqzlhzxlwyxzhdnlrxhxjcdyhlbrlmbrllaxksllljlyxxlycrylcjcgjcmtlzllcyzzpzpcyawhjjfybdyyzsepckzdqyqpbpcjpdcyzbdbbcyydycnnpjmtmlrmfmmgwygbsjgygsmdqqqztxmkqwgxllpjgzbqcdjjjfpkjkcxbljmswmdtqjxldlppbxcwkcqqbfqjczagzgmykbhyyhzykndkzmbpjyspxthlfpnyygxjdbkxnhhjhzjxstrstldxskzysybmxjlxyslbzyslhxjpfxbqnbylljqkygzmcyzzymccsldlhzgwfwyxzmwcxtynxjhbyymcysbmhysmydyshqyzchmjjmzcaahcbjbbhplxtylsxsdjgjdhkxxtxxnbhnmlngsltxmrhnlxqjxmzllyswqgdlbjhdcgjyqycmhwfwjybbbyjmjwjmdpwhxqldyapdfxxbcgjspckrssyzjmslbzzjfljjjlgxzgyxyxlszqyxbexyxhgcxbpldyhwecdwwcjmbtxchxyqxllxflyxlljlssfwdpzsmyjclmswtczbchqekcqbwlcgydblqppqzqfjqdjhymmcxtxdrmjwrhxcjzclqxdyynhyyhrslsrsywwzjymtltllgzqcjzyabsckzcjyccqljsqxalmzyyywlwdxzxqdllqshgpjfjljhjabcqzdjgthhsstcyjlbswzlxzxrwgldlzrlzqtgsllllzlymxqgdzhgbdbhzpbrlw xqbpfdwo  whlypcbjcc dmbzpbzz cyqxldomzblzwpdwyygdstthcsqsccrsssyslfybfntyjszdfndpthtzzmbblxlcmyffgtjjqwftmdpjwdnlbzcmmctgbdzlqlpyfhsymjylsdchdzjwjcctljcldtljjcpddpjdsszynndbjlggjzxsxnlycybjjqxcbylzcfzppgkcxzdzfztjjfjsjxzbnzyjqttyjwhtyczhymdjxttmpxsflzcdwslshxybzgtfmlcjtacbbmgdewycyzcdszcyhflyctygwhkjyylsjcxgywjcbhlcsnddbtzbsclyzczzssqdllmqyyhfllqllxfdyhabxggnywyypllsdldllbjcyxjzmlhljdxyyqytdlllbbgbfdfbbqjzzmdpjhgclgmjjpgaehhbwcqxaxhhhzchxyphjaxhlphjpgpzjqcqzgjjzzgzdmqyybzzphyhybwhazyjhykfgdpfqsdlzmljxjpgalxzdaglmdgxmwzqytxdxxpfdmmssympfmdmmkxksyzyshdzkjsysmmzzzmsydnzzczxbmlstmddnmxckjmztyymzmzzmsshhdccjemxxkljstgwlsqlyjzllsjssdbpmhnlyjczyhmxxhgzcjmdhxtkgrmxfwmckmwkdcksxqmmmfzzydkmsclcmpcgmwrpxqpzdsslcxkyxtmlgjyahzjgzqmcsnxyhmmpmlkjxmhlmlgmxctkzmjjyszjsyszhsyjzjcdajzybsdqjzgwzkgxfkdmsdjlfmehkzqkjbeypzyszcdwyjffmzjykttdzzefmzlbnpplplpbpszalltylkckqzkgenqlwagxxydpxlhsxqqwqykxqclhyxxmlyccwlymqyskychlcjnszkpyzkcqzqljbdmdjhlasqlbydwqlwdnbqcrydddtjybkbwszdxdtnpjdtctqdfxqqmgnseclstbhpwslctxxlpwydzklzygzcqapllkccylbqmqczqcljslqzdjxldthpzqdljjxzqdjyzhkzljcyqdyjppypeakjyrmpcbymcxkllzllfqpylllmbsglcysslrsysqtmxyxqqzbdzrysyztffmzzsmzqhzssccmlyxwtpzgxzjgzgsjsgkddhtqggzllbjdzlcbzhyxyzhzfywxyzymsdbzzyjgtsmtfxqyxjscdgslnmdlrytzlryylxqhtxsrtzcgyxbnqqzfhykmzjbzymkbpnlyzpblmcnqyzzzsjzhjctzhhyzzjrdyzhnfxglfxslkgjtctssyllgzrzbbjzzklpkbczyslxyxbjfpnjzzxcdwxzyjxzzdjjgggrsrjkmcmzjlsjywqshyhqjsxpjzzzlsnshrnypjtwchklbsrzlcxwjqxqkysjycztlqzybbybwzjqdwgyzcytjcjxckcwdkkzxsgkdzxwwyyjqyytcytdjlxwkczkklcclzcqqdzlqlcsfqchqhsfsmqzzllbjjzbsjhtsjdysjqjpdlzcdcwjkjzzlpycgmzwdjjbsjqzsyzyhhxcbbjydssddzncglqmbtsfcbfdzdlznfgfjgfsmptjqlmblgqcyyxbqkdxjqsrfkztjdhczklbsdzcfytplljgjhtxzcsszzxstcygkgckgyoqxjplzbbbgtgyjdgczqszlbjlsjfzgkqqjcgyczbzqtldxrjxbsxxpzxhyzyclwdsjjhxmfczpfzhqhqmqgkslyhtycgfrzgnqxclpdlbzcsczqlljblhbdcypczppdymtzsgyhckcpzjgslclnscdsldzxbmsdlddfjmkdjdhslzxlszqpqpgjllybdszgqlbzlslkyyhzttncjyqtzzfszqztlljtyyllqllqyzqlbdzlslyyzymdfszsnhlxznczqzbbwskrfbcyzmthblgjpmczzcstlxshtzcyzlzblfeqhlxflcjlyljqcbzlzjghsstbrmhxzhjzclxfnbgxgtqjcztmsfzkjmssnxljkbhszxntnlzdntlmsjxgzjyjczxyhyhwrwwqnztnfjscpzshzjfyrdjsfscjzbjfzqzchzlxfxsbzqlzsgyftzdcszxzjbqmszkjrhxjzcgbjkhchgtjkjqglxbxfgdrtylxjxgdtsjxhjzjjcmzlcqsbtxhqgxttxhxftsdkfjhzyjfjxrzcdlllcqsqqzqwqxswqtwgwbzcgcllqzbclmqqtzgzxzxljfrmyzflxysqxxjkxrmjdcdmmyxbsqbhgcmwfwtgmxlzbyytgzyccdxyzxswgxyjyznbgpzjcqsyxcxrtfycgrhztxszzthcbfclsyxzljqmzlmplmxzjssflbysmyqhxjsxrxsqzzzsslyflczjrcrxhhzxqydshxsjjhzcxjbdynsysxjbqlpxzqpymlxzkyxlxcjlcycrxzzlldlllsjyhzxgyjwkjrwyhcpsgnrzlfzwfzznsxgxflzsxzzzbfcsyjdbrjkrdhhgxjljjtgxjxxstjtjxlyxqfcsgswmsbctlqzzwlzzkxjmltmjyhsddbxgzhdlbmyjfrzfcgclyjbpmlysmsxlszjqqhjzfxgfqfqbpxzgyyqxgztcqwyltlgwwgwhllfsfgzjmgmgbgtjfsyzzgzyzaflsspmlbflcwbjzcljjmzlpjjlymqdmyyyfbgygqzglyzdxqyxrqqqhsxyyqqygjtyxfsfsllgnqcygycwfhcccfxbylypllzqxxxxxkqhhxshjdcfdsczjxcpzwhhhhhapylhalpqafyhxdyllkmzqgggddesrnndltzgchybpysqjjhclljtolnjpzljlhymheydydsqycddhgzpndzclzywllznteytgxlhslpjjbdgwxpcdntjcklkclwkllcasstknzdnqnttlyyzssysszzryljqkcgbhhcrxrzydgrgcwcgzhfffppjfzynakrgywyqpqxxfkjtszzxswzddfbbqtbgtzfznpzfpzxzpjszbmqhkcyxyldkljnypkyghgdcjxxeahpnzgctzcmxcxmmjxnkszqnmnlwbwwxjjyhclstmcsqdjcxxtpcnpdtnnpglllzcjlspblplkcdtnjnlyyrscffjfqwdpgzdwmnzcclodaxnssnyzrestyjwjyjdbcfxnmwttbqlwstszgybljpxglboclgpcbjftmxzljylzxcltpnclcgxtfzjshcrxsfyszdkntlbyjcyjllstgqcbxnwzxbxklylhzlqzlnzcqwgzlgzjncjgcmnzzgjdzxtzjxycyycxxjyyxjjxsssjstssttppghtcsxwzdcsyfptfbchfbblzjclzzdbxgcxlqpxkfzflsyltywbmnjhskbmddbcysccldxycddqlyjjhmqllcsgljjsyfpyyccyltjantjjpwycmmgqyysqdhqmzhszxpftwwzqswqrfkjlxjqqyfbrxjhhfwjgzyqacmyfrhcyybyqwlpexcczstyrltsdmqlykmbbgmyyjprknnbbsxyxbhyzdjdnghpmfsgbwfzmfjmmbcmzzcjjlcnyxyqgmlrygqccyhzlwjgcjcggmcjjfyzzjhycfrrcmtzqzxhfqgdjxccjeaqcrjthpljlszdjrbcqhjdzrhxlyxjsymhzydwldfryhbbydtssccwbxglpzmlzztqsscpjmmxjcsjytycghycjwsnsxlfemwjnmkllswtxhyyygcmmcwjdqdjzglljwjnkhpzggflccsczmcbltbhbqjxqdjpdjqtghglfqawbzyjjltstdhqhctcbchflqmpwdshyytqwcnztjtlbypbpdyyyxsqkxwyyflxxncwcxybmaelykkjmzzzbrxyaqjfljpfhhhytzzxrgqqmhspgdzjwbwpjhzjdyscqwzkthxsqlzyymysdzgrxckkhjlwpysyscsyzlrmlqsyljxbcxtlhdqzpcycykpppnsxfyzjjrcemhszmsxlxglrwgcstlrsxbygbzgztcpldjlslylymdtmtcpalcxpqjcjwtcyyzlblxbzlqmyljbghdslssdmxmbdczsxwhamlczcpjmcnhjyjnsygchskqmzzqdllkablwjqsfmocdxjrrlyqchjmybyqlrhetfjzfrfksryxfjdwdsxxlwsqjyslyxwjhsnlxyyxhbhawhhjcxwmyljcsqlkydttxbzsxfdxgxsjhhsxxybssxdpwncmrptjzczenygcxqfjxkjbdmljcmqqxloxslyxxlylljdzbtymhbfsttqqwlhogyblscalzxqlhtwrrqhlstmypyxjjxmqsjpnbryxyjllyqylthylqyfmhkljdmllhfzwkzhljmlhljkljstlqxylmbhhlnlsxqchxcfxxlhyhjjgbyzzkbxscqdjqdsxjzsyhzhhmgsxcsymxfebcqwwrbpyyjqtyqcyjhqqzyhmwffhgzfrjfcdbxndqyzpcyhhjlfrzgppxzdbbgzqstlgdgylcqmgchhmfywlzyxkjlypqhsywmqqgqzmlzjnsqxjqsyjtcbehsxfssfxzwfllbcyyjdytdthwzsfjmqqyjlmqsxlldttkhhybfpwdyysqqrnqwlgwdebdwcyygcdlkjxtmxmyjsxhybrwfymwfrxyqmxysctzztfykmldhqdlwyqnlcryjblpsxcxywlsbrrjwxhqybhtydnhhgmmywytzcsqmtssccdalwztcpqpyjllqzyjswxwzzmmglmxclmxczmxmzsqtzppjqblpgxjzhfljjhycjsnxwcxsccdlxsyjdcqcxslqyclzxlzzxmxqrjmhrhzjphmfljlmlclqnldxzlllfypngjysxcqqdcmqjzzxhnpnxzmekmxxykyqlxsxtxjxyhwdcwdzhqyybgybcyscfgfsjnzdyzzjzxrzrqjjymcanhrjtldbpyzbstjhxxzypbdwfgzzrpymtngxzqbyxmbbfcckrjjjbjegrzgyclkxzdxkknsjkcljspgyyzlqqjybzssqlllkjfcbktylcccdblsppfylgydtzjyqggkqttfcxbdkdxxhybbfytyhbclpdytgdhryrnjsbtcsnyjqhklllzslydxxwbcjqsbxbfjzjcjdzfbxxbrmlazgcsnclbjdstblfrzdswsbxbcllxxlzdjzsjpylyxxyftfffbhjjjgbygjpmmmmsscljmtlyzjxswxtyledqpjmygqzjgdjlqjwjqllsdgjgygmscljjxdtygjqjqjcjzcjgdzdshqgsjggcjhqxsnjlzzbxhsgzxcxyljxyxyydfqqjhjfxdhctxjyrxysqtjxyefyyssyxjxncyzxfxcsyszxyyschshxzzzgzzzgfjdldylnpzgyjyzyyqzpbxqbdztzczyxxyhhscxshcggqhjhgxwsztmzmehyxgebtylzkkwytjzrclekestdbcykqqsayxcjxwwgsbhjszsdhcsjkqcxswxfctynydpzcczjqtzwjqdzzzqzljchlsbhpydxpsxshhezdxfptjqyzzxhyaxncfzyyhxgnqmywxtzsjpkhhgymxmxqcxtsbcqsjyxhtyylybcqlmmszmjzjllcogxzaajzyhjmchhcxzsxzdznleyjjzjbhzwzzsqtzpsxztdsxjjjznyazphhyysrnqdthzhayjyjhdzxzlswclybzyecwcycrylcxnhzydzydyjdfrjjhtrsqtxyxjrjhojynxelxsfsfjzghpzsxzszdzcqzbyyklsgsjhczshdgqgxyzgxchxzjwyqwgyhksseqzzndzfkwyssdclzstsymcdhjxxyweyxczaydmpxmdsxybsqmjmzjmtzqlpjyqzcgqhxjhhhxxhlhdldjqsldwbsxfzzyyschtytyjbhecxhjkgjfxbhyzjfxbwhbdzfyzbcapnpgnydmsxhkhhmamlnbyjtmpxyjmcthjbzyfcgtyhwphftgzzezsbzegpbmdskftycmhbllhgpzjxzjgzjyxzsbbqsczzlzccstpgxmjsftcczjzdjxcybzlfcjsyzfgszlybcwzzbyzdzypswyjgxzbdsysxlgzbzfygczxbzhzftpbgzgejbstgkdmfhyzzjhzllzzgjqzlsfdjsscbzgpdlfzfzszyzyzsygcxsntxchczxtzzljfzgqsqyxzjqccccdjcdxzjyqjccgxztdlgscxzsyjjqtcclqdqztqchqqjztezzzpbkkdjfcjfztybqyqttynlmbdktjcpqzjdzfpjsbnjlgyjdxjdzqkzgqkxclpzjtcjdqbxdjjjstcjnxbxcmslyjcqmtjqwwcjjnjnlllhjcwqtbzqyczczpzzdzyddcyzdzccjgtjfzdprntctjdcqtqndtjnplzbcllctdsxkjzqdpzlbznbtjdcxfczdbccjjltqjpldcgzdbbzjcqdcjwynllzlzccdwllxwzlxrsntqjccxkjlsgdfqtddglrlajjtklymkqlldzytdyycygjwyxdxfrskstcdenqmrkqzhhqkdldazfkypbggpzrebzzykyzspegjjglkqzzzslysywyzwfqznlzzlzhwcgkypqgnpgblplrrjyxcccgyhsfzfwbzywtgzxyljczwhxzjzblfflgskhyjzeyjhlpllllcygxdrzelrhgklzzyhzlyqszzjzqljzflnbhgwlczcfjwspyxnlzlxgccpzbllcxbbbbxbbcbbcrnncccyrbbsyldcgqyyqxygmqzwtzydyjhyfwdehzdjywlccntzyjjcdedpzdztstqjhdymbjnyjzlxtsstphndjxxbyxqtzqddtjtdyztgwscszqflshlglbcjbhdlyzjyckwtydylbnydsdsycctyszyyebgexhqddwnygyclxtdcystqmygzasccszzddlcclzrqxyyeljsbymxshztembbllyyllytdqyshymrqxkfkbfxnxsbychxbwjyhtqbpbsbwdzylkgzskyghqzjhhxjxgnljkzlyycdxlfwfghljgjybxblybxqpqgztzplncybxdjyqydymrbesjyyhkxxstmxrczzywxyhybmcflyzhqyzmqxdbxbzwzmslpdmyckfmzklzcyjycclhxfzlydqzpzygyjyzmzxdzfyfyttqtchgspczmlccytzxjcytjmkslpzhysnwllytpzctzzcktxdhxxtqcypksmqccyyazhtjpcylzlyjbjxtfnyljyynrxcylmmnxjsmybcsysslzylljjgyldzdpqbfzzblfndsqkczfhhhgqmrdsxycstxnqqjpyjbfcxdyqfbnxejdgyqbsrcnfyyqpghyjdyzxgrhtkyleqdzntsmgklbsgbpyszbytjzsszjcssxzbhbscsbzczptqfzmqflypybbjgszmxxdjmthyskkbjtxhjcegbsmjyjzcxtmljyxrzzqscxxqptzxmkyxxxjcljprmyygadyskqlsadhrskqxzxztcghztlmlwxybwsycdbhjhcfcwzsxhytkzlxqshlyczjxemplprcgltbzztlzjcyjgdtclglpllqpjmzpapxyzlaktkdnczzbnzctdqqzjyjgmctxltgcszlmlhbglkfwnwzhdxphlfmkydlgxdtwzfrjejctzhydxykshwfzcqshktmqqhtchymjdjskhxzjzbzzxympajqmsdbxlsklyynwrtsqlscbpdbsgzwyhtlkssswhzzlyytnxjgmjszsxfwnlsoztxgxlsammlbwldszylakqcqctmycfjbslxclzjclxxksbzqclhjphqplsxsckslnhpsfqqytxjjzlqldxzjjzdyydjnzptfcdskjfsljhylzqjzlbthydgdjfdbyazxdzhzjnhhqbyknxjjqczmlljzkspldsclbblxklelxjlbjycxjxgcnlcqplzlznjtzljgyzdzpltqcssfdmnycxgbtjdcznbgbqyqjwgkfhtnbyqzqgbkpbbyzmtjdytblsqmbsxtbnpdxklemyycjynzdtldykzzxddxhqshdgmzsjycctayrzlpwltlkxslzcggexclfxlkjrtlqjaqzncmbqdkkcxglczjzxjhptdjjmzqykqsecqzdshhadmlzfmmzbgntjnnlgbyjbrbtmlbyjdzxlcjlpldlpcqdhlhzlycblcxzcjadqlmzmmsshmybhbskkbhrsxxjmxsdznzpxlbbragggfchgmsklltsjyycqlcskywyehywhbhqywbawykqldqftntkhqcgdqktgpkxhcpdhtwtmssyhbwcrwxhjmkmzngwtmlkfghkjyldyycxwhyeclqhkqhtdqhhffldxqwgzyydesbpkyrzpjfyyzjceqdzzdlattbbfjllcxdlmjsdxegygsjqxcfbxsszpdyzcxdnyxpfzydlyjccpltxlsxyzyrxcyysdylwwndsahjsygyhgywkaxtjzdaxysrltdjssaxfnejdxyzhlxlllzhzsjnyqyqqxyjghzgjcyjchzlycdshwsgczyjxcllnxzjjyyxnfsmwfpylcyllabwddhwdxjmcxztzpmlqzhsfhzynztlldywlslxhymmylmbwwkyxyadtxylldjpybpwfxjmmmllhafdllaflbhhhbqqjtzjcqjjdjtffkmmmbythygdcqrddwrqjxnbysnmzdbyytbjhpybygtjxaahgqdqtmystqxkbtsbkjlxrbeqqhqmjjbdjwtgtbxpgbktlgqxjjjcdhxqdwjlwrfmqgwqhckryswgbtgygbwsdwdwrfhwytjjxxxjyzyslphyypayxhydqkxshxyxeskqhywbdddpplcjlhqeewxksyyhdyplfjthkjltcyyhhjttpltzzcdlthqkcxqysteeywkyzyxxyysddjkllpwmcyhqgxyhcrmbxpllnqydqhxsxxwgdqbshyllpjjjthyjkyphthyyktyezyenmdshlcrpqfbgfxzbsbtlgxsjbswyysksflxlpplbbblbsfxfyzbsjssylpbbffffsscjdstzsxtryjcyffsytyzbjtbctsbsdhrtjjbytcxyjeylxcbnebjdsysyhgsjzbxbytfzwgenyhhthjhatfwgcstbgxklstywmtmbyxjskzscdyjrcytwxzfhmymcxlznsdjtttxrycfyjsbsdyerxhljxbbdeynjghxgckgscymblxjmsznskgxfbnbbthfjaafxyxfpxmyfhdtzcxzzpxrsywzdlybbjtyqwqjpzypzjznjpzjlztfysbttslmptzrtdxqsjehbzylzdhljsqmlhtxtjecxalzzspktlzkqqyfsygywpcpqfhqhytqxzkrsgtgsqczlptxcdyyzssqzslxlzmacbcqbzyxhbsxlzdltcdjtylzjyytpzylltxjsjxhlbmytxcqrblzssfjzztnjydxmyjhlhpblcyxqjqqkzzscpzkswalqsblcczjsxgwwwygyatjbbctdkhqhkgtgpbkqyslbxbbckbmllxdzstbklggqkqlsbkkdfxrmdkbftpzfrtbbmferqgxkjpzsstlbzdpszqzsjthljqlzbpmsmmsxlqqnhknblrddnhxdhddjcyygyfqgzlgsygmjqgkhbpmxyxlytqwlwgcpbmjxcyzydrjbhtdjxeeshtmjsbyplwhlzffnypmhxqhpltbqpfbcwjdbygpnxtbfzjgsddtjshxeawzzyllttybwjkgxghlfkxdjtmszsqynzggswqsphtlsskmclzxyszqzxncjdqgzdlfnykljcjllzlmzznhydsshthxzlzzbbhqzwwycrdhlyqqjbeyfsgxthsrxwqhwfslmssgzttyeyqqwrslalhmjtqjsmxqbjjzjxzyzkxbyqxbjxshzssfglxmxzxfghkzszggylclsarjxhslllmzxelglxydjytlfbhbpnlyzfbbhptgjkwetzhkjjxzxxglljlstgshjjyqlqzfkcgnndjsszfdbctwwseqfhqjbsaqtgypjlbxbmmywxgslzhglzgnyfljbyfdjfrgsfmbyzhqfbwjsyfyjjphzbyyzffwodgrlmftmlbzgycqxcdjygzyyyytytydwegazyhxjlzythlrmgrjxzclhneljjthtbwjybjjbxjjtjteekhwsljplpsfazpqqbdlqjjtyyqlyzkdksqjyyjzldqcgjjyzjsycmraqthtejmfctyhypkmhycwjdcfhyyxwshctxrljgjshccyyyjltkttytmxgtcjtzayyoczlylbszywjytsjyhbyshfjlygjxxtmzyyltxxypclxyjzyzyypnhmymdyylblhlsyygqllnjjymsoycbzgdlyxylcqyxtszegxhzglhwbljgeyxtwqmakbpqcgyshhegqcmwyywljyjhyyzlljjylhzyhmgsljljxcjjyclycjpcpzjzjmmylcjlnqljjjlxxjmlszljqlycmmhcfmmfpqqmfxlqmcffqmmmmhmznfhhjgtthhkhslnchhyqdxtmmqdcydyxyqmyqylddcyyydazdcymzydlzfffmmycqcwzzmabtbyctdmndzggdftypcgqyttssffwbdtzqssystwjjhjytsxxylbyqhwwhxezxwznnqzjzjjqjccchyyxbzxccyjtllcqxknjycyycynzzqyyoewyczdcjycchyjlbtzkycqwlpgpyllgkdldlgkgqbgychjxy";

        private static readonly string[] Syllables = new string[]
        {
            "", "yi", "ding", "kao", "qi", "shang", "xia", "han", "wan", "zhang", "san", "ji", "bu", "yu", "mian", "gai", "chou", "zhuan", "qie", "pi", "shi", "qiu", "bing", "ye", "cong", "dong", "si", "cheng", "diu", "liang", "you", "yan", "sang", "gun", "jiu", "ge", "ya", "qiang", "zhong", "jie", "feng", "guan", "chuan", "chan", "lin", "zhuo", "zhu", "ba", "dan", "wei", "jing", "li", "ju", "pie", "fu", "nai", "wu", "tuo", "me", "zhi", "zha", "hu", "fa", "le", "yin", "ping", "pang", "qiao", "guai", "mie", "xi", "xiang", "shu", "dou", "nang", "jia", "mao", "mai", "luan", "zi", "ru", "xue", "sha", "na", "gan", "suo", "cui", "zhe", "qian", "gui", "jue", "ma", "zheng", "er", "chu", "kui", "yun", "sui", "gen", "xie", "tou", "wang", "kang", "da", "jiao", "hai", "heng", "mu", "ting", "qin", "bo", "lian", "duo", "ren", "shen", "ze", "jin", "pu", "reng", "fo", "lun", "cang", "zai", "ta", "xian", "hong", "tong", "dai", "ling", "chao", "chang", "sa", "men", "fan", "yang", "wo", "jian", "yao", "fen", "di", "fang", "pei", "diao", "dun", "xin", "ai", "xiu", "tang", "huo", "hui", "che", "xun", "nu", "gu", "ni", "ban", "xu", "zhou", "qu", "ci", "beng", "ga", "dian", "bi", "zhao", "zuo", "ti", "zhan", "he", "she", "gou", "ning", "yong", "wa", "ka", "bao", "lao", "bai", "ming", "hen", "quan", "tiao", "xing", "kan", "lai", "chi", "kua", "guang", "mi", "an", "lu", "mou", "cha", "gong", "cun", "lv", "zhen", "ce", "kuai", "chai", "nong", "hou", "jiong", "tui", "nan", "xiao", "bian", "cu", "e", "ku", "jun", "zu", "hun", "su", "lia", "pai", "biao", "fei", "bei", "zong", "tian", "dao", "tan", "chui", "peng", "kong", "juan", "luo", "song", "leng", "ben", "cai", "zhai", "qing", "ying", "ruan", "chun", "ruo", "pian", "sheng", "huang", "duan", "ou", "za", "lou", "sou", "yuan", "rong", "jiang", "bang", "hao", "shan", "bin", "nuo", "can", "lei", "zao", "ao", "chuang", "piao", "man", "zun", "liao", "tie", "seng", "min", "sai", "dang", "xuan", "tai", "lan", "meng", "qiong", "lie", "kuang", "zan", "chen", "teng", "long", "rang", "xiong", "chong", "dui", "ke", "tu", "nei", "liu", "shou", "ran", "gang", "gua", "kou", "pan", "qia", "mei", "zhun", "cou", "du", "kai", "deng", "wen", "hua", "yue", "bie", "pao", "geng", "shua", "cuo", "la", "pou", "tuan", "mo", "keng", "shao", "gao", "lang", "weng", "tao", "nao", "zang", "suan", "nian", "shuai", "ang", "que", "zui", "rou", "shuang", "die", "rui", "po", "mang", "tun", "fou", "shun", "guo", "pen", "ne", "m", "huai", "pin", "ha", "yo", "o", "shuo", "huan", "nou", "ken", "a", "chuai", "pa", "se", "nie", "wai", "miao", "n", "cao", "de", "hei", "ceng", "hm", "ca", "chuo", "zen", "lo", "nin", "kun", "qun", "ri", "lve", "zhui", "hang", "sao", "zeng", "zhuang", "en", "zou", "nv", "nuan", "niu", "rao", "niang", "niao", "nen", "sun", "kuan", "cen", "cuan", "shui", "kuo", "te", "re", "zei", "den", "zhua", "shuan", "zhuai", "rua", "zuan", "shai", "sen", "run", "chua", "nve", "gei", "neng", "fiao", "ei", "miu", "eng", "nun"
        };

        private static ushort[]? _indices;
        private static Dictionary<char, char>? _tradToSimp;
        private static readonly object _tradInitLock = new object();

        private static Dictionary<char, char> EnsureTradToSimp()
        {
            var existing = _tradToSimp;
            if (existing != null) return existing;

            lock (_tradInitLock)
            {
                if (_tradToSimp != null) return _tradToSimp;

                int count = Math.Min(TradSimpTable.TradChars.Length, TradSimpTable.SimpChars.Length);
                var map = new Dictionary<char, char>(count);
                for (int i = 0; i < count; i++)
                {
                    map[TradSimpTable.TradChars[i]] = TradSimpTable.SimpChars[i];
                }

                _tradToSimp = map;
                return map;
            }
        }

        /// <summary>
        /// 将单个繁体汉字规范化为对应简体汉字（非繁体字返回原字符）
        /// </summary>
        public static char MapToSimplified(char c)
        {
            var map = EnsureTradToSimp();
            return map.TryGetValue(c, out char simp) ? simp : c;
        }

        /// <summary>
        /// 将字符串中的繁体汉字规范化为简体（保留英文、数字与标点）
        /// </summary>
        public static string ToSimplified(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var map = EnsureTradToSimp();
            if (map.Count == 0) return text;

            StringBuilder? sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (map.TryGetValue(c, out char simp))
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder(text.Length);
                        sb.Append(text, 0, i);
                    }
                    sb.Append(simp);
                }
                else
                {
                    sb?.Append(c);
                }
            }
            return sb != null ? sb.ToString() : text;
        }
        private static readonly object _initLock = new object();

        internal static void Prewarm()
        {
            EnsureInitialized();
        }

        private static void EnsureInitialized()
        {
            if (_indices != null) return;
            lock (_initLock)
            {
                if (_indices != null) return;
                try
                {
                    var asm = typeof(PinyinEngine).Assembly; string? resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("pinyin.bin", StringComparison.OrdinalIgnoreCase)); if (resName != null) using (var stream = asm.GetManifestResourceStream(resName))
                    {
                        if (stream != null)
                        {
                            byte[] raw = new byte[stream.Length];
                            stream.Read(raw, 0, raw.Length);
                            ushort[] arr = new ushort[raw.Length / 2];
                            Buffer.BlockCopy(raw, 0, arr, 0, raw.Length);
                            _indices = arr;
                            return;
                        }
                    }
                }
                catch { }
                _indices = new ushort[CJK_COUNT];
            }
        }

        /// <summary>
        /// 获取单字的拼音首字母（小写 a-z，非汉字返回字符本身或空格）
        /// </summary>
        public static char GetInitial(char c)
        {
            c = MapToSimplified(c);
            int code = c;
            if (code >= CJK_START && code <= CJK_END)
            {
                return InitialsTable[code - CJK_START];
            }
            return char.ToLowerInvariant(c);
        }

        /// <summary>
        /// 获取单字的全拼（小写，非汉字返回原字符）
        /// </summary>
        public static string GetPinyin(char c)
        {
            c = MapToSimplified(c);
            int code = c;
            if (code >= CJK_START && code <= CJK_END)
            {
                EnsureInitialized();
                ushort idx = _indices![code - CJK_START];
                return Syllables[idx];
            }
            return char.ToLowerInvariant(c).ToString();
        }

        /// <summary>
        /// 将文本转换为拼音首字母缩写字符串
        /// </summary>
        public static string ToInitials(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c)) continue;
                sb.Append(GetInitial(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 将文本转换为小写无声调全拼字符串（保留并小写化非汉字字符，避免逐字符拆箱分配）
        /// </summary>
        public static string ToFullPinyin(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            EnsureInitialized();
            ushort[] indices = _indices!;
            var sb = new StringBuilder(text.Length * 4);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c)) continue;
                c = MapToSimplified(c);
                int code = c;
                if (code >= CJK_START && code <= CJK_END)
                {
                    sb.Append(Syllables[indices[code - CJK_START]]);
                }
                else
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }

        private static readonly Dictionary<string, (string lower, string initials, string fullPy)> _searchNormCache
            = new Dictionary<string, (string, string, string)>(512, StringComparer.Ordinal);
        private static readonly object _searchCacheLock = new object();

        private static string StripSeparators(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]) || char.IsPunctuation(text[i]))
                {
                    var sb = new StringBuilder(text.Length);
                    sb.Append(text, 0, i);
                    for (int j = i + 1; j < text.Length; j++)
                    {
                        char c = text[j];
                        if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c))
                        {
                            sb.Append(c);
                        }
                    }
                    return sb.ToString();
                }
            }
            return text;
        }

        private static void GetNormalizedSearchForms(string targetText, out string targetLower, out string initials, out string fullPy)
        {
            lock (_searchCacheLock)
            {
                if (_searchNormCache.TryGetValue(targetText, out var cached))
                {
                    targetLower = cached.lower;
                    initials = cached.initials;
                    fullPy = cached.fullPy;
                    return;
                }
            }

            string simplified = ToSimplified(targetText);
            targetLower = simplified.ToLowerInvariant();
            initials = ToInitials(simplified);
            fullPy = ToFullPinyin(simplified);

            lock (_searchCacheLock)
            {
                if (_searchNormCache.Count >= 4096)
                {
                    _searchNormCache.Clear();
                }
                _searchNormCache[targetText] = (targetLower, initials, fullPy);
            }
        }

        /// <summary>
        /// 综合检索匹配：检查目标文本是否匹配关键词（支持繁简中文、英文大小写、拼音首字母与全拼极速命中）
        /// </summary>
        public static bool Matches(string targetText, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (string.IsNullOrEmpty(targetText)) return false;

            if (!ModFeatureConfig.EnablePinyinSearch)
            {
                string cleanQuery = query.Trim();
                if (cleanQuery.Length == 0) return true;
                return targetText.IndexOf(cleanQuery, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            string normQuery = ToSimplified(query.Trim()).ToLowerInvariant();
            if (normQuery.Length == 0) return true;

            GetNormalizedSearchForms(targetText, out string targetLower, out string initials, out string fullPy);

            // 1. 直接中英文字符串包含
            if (targetLower.IndexOf(normQuery, StringComparison.Ordinal) >= 0) return true;

            // 2. 拼音首字母与全拼包含
            string compactQuery = StripSeparators(normQuery);
            if (compactQuery.Length == 0) return true;

            if (initials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0) return true;
            if (fullPy.IndexOf(compactQuery, StringComparison.Ordinal) >= 0) return true;

            return false;
        }
    }
}
