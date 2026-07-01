/*
  Illustrator batch exporter for Tongyuan card frames.

  Usage:
  1. Open your AI/SVG card template in Adobe Illustrator.
  2. File > Scripts > Other Script...
  3. Choose this JSX file.
  4. PNG frames are exported to D:/工程/TY/art/cards.

  The script preserves fixed type labels such as 斩击/突刺/打击/远程/技能/能力,
  and hides placeholder title/body/cost text before export.
*/

#target illustrator

(function () {
    if (app.documents.length === 0) {
        alert("请先在 Illustrator 中打开卡牌模板文件。");
        return;
    }

    var doc = app.activeDocument;
    var outDir = new Folder("D:/工程/TY/art/cards");
    if (!outDir.exists) outDir.create();

    var originalArtboard = doc.artboards.getActiveArtboardIndex();
    var originalArtboardCount = doc.artboards.length;
    var hiddenText = [];
    var hiddenGreen = [];

    try {
        var frames = detectCardFrames(doc);
        if (frames.length !== 6) {
            throw new Error("识别到 " + frames.length + " 个卡框，应为 6 个。请确认每张卡有 172x300 左右的外框矩形。");
        }

        hiddenText = hideDynamicText(doc);
        hiddenGreen = hideGreenArtPlaceholders(doc);

        for (var i = 0; i < frames.length; i++) {
            var f = frames[i];
            var rect = expandBounds(f.bounds, 3);
            var idx = doc.artboards.length;
            doc.artboards.add(rect);
            doc.artboards.setActiveArtboardIndex(idx);

            var file = new File(outDir.fsName + "/" + f.name + ".png");
            var opts = new ExportOptionsPNG24();
            opts.artBoardClipping = true;
            opts.transparency = true;
            opts.antiAliasing = true;
            opts.horizontalScale = 400; // export oversized frames, then let Godot downsample cleanly
            opts.verticalScale = 400;
            doc.exportFile(file, ExportType.PNG24, opts);
        }
    } catch (e) {
        alert("导出失败：\\n" + e + "\\n行号：" + (e.line || "未知"));
    } finally {
        for (var j = doc.artboards.length - 1; j >= originalArtboardCount; j--) {
            try { doc.artboards[j].remove(); } catch (e2) {}
        }
        restoreHidden(hiddenText);
        restoreHidden(hiddenGreen);
        try { doc.artboards.setActiveArtboardIndex(Math.min(originalArtboard, doc.artboards.length - 1)); } catch (e3) {}
    }

    alert("导出完成。请检查：\\n" + outDir.fsName);
})();

function detectCardFrames(doc) {
    var candidates = [];
    for (var i = 0; i < doc.pathItems.length; i++) {
        var p = doc.pathItems[i];
        if (p.hidden || p.guides || p.clipping) continue;

        var b = p.visibleBounds; // [left, top, right, bottom]
        var w = Math.abs(b[2] - b[0]);
        var h = Math.abs(b[1] - b[3]);

        if (w > 155 && w < 190 && h > 285 && h < 315) {
            candidates.push({
                item: p,
                bounds: b,
                cx: (b[0] + b[2]) / 2,
                cy: (b[1] + b[3]) / 2,
                w: w,
                h: h
            });
        }
    }

    candidates.sort(function (a, b) {
        // Top row first, then left to right.
        if (Math.abs(a.cy - b.cy) > 80) return b.cy - a.cy;
        return a.cx - b.cx;
    });

    if (candidates.length > 6) {
        // Keep the six most card-like rectangles, then sort again.
        candidates.sort(function (a, b) {
            var da = Math.abs(a.w - 172) + Math.abs(a.h - 300);
            var db = Math.abs(b.w - 172) + Math.abs(b.h - 300);
            return da - db;
        });
        candidates = candidates.slice(0, 6);
        candidates.sort(function (a, b) {
            if (Math.abs(a.cy - b.cy) > 80) return b.cy - a.cy;
            return a.cx - b.cx;
        });
    }

    var names = [
        "frame_slash",
        "frame_thrust",
        "frame_skill",
        "frame_blunt",
        "frame_ranged",
        "frame_ability"
    ];
    for (var n = 0; n < candidates.length && n < names.length; n++) {
        candidates[n].name = names[n];
    }
    return candidates;
}

function expandBounds(bounds, pad) {
    return [
        bounds[0] - pad,
        bounds[1] + pad,
        bounds[2] + pad,
        bounds[3] - pad
    ];
}

function hideDynamicText(doc) {
    var hidden = [];
    var keep = {
        "斩击": true,
        "突刺": true,
        "打击": true,
        "远程": true,
        "技能": true,
        "能力": true
    };

    for (var i = 0; i < doc.textFrames.length; i++) {
        var t = doc.textFrames[i];
        var s = String(t.contents).replace(/\s+/g, "");
        if (keep[s]) continue;
        if (s === "3" || s.indexOf("请输入文本") >= 0 || s.indexOf("滚滚长江") >= 0 || s.length === 0) {
            hidden.push({ item: t, hidden: t.hidden });
            t.hidden = true;
        }
    }
    return hidden;
}

function hideGreenArtPlaceholders(doc) {
    var hidden = [];
    for (var i = 0; i < doc.pageItems.length; i++) {
        var item = doc.pageItems[i];
        try {
            if (item.typename !== "PathItem" || !item.filled) continue;
            var c = item.fillColor;
            if (!c || c.typename !== "RGBColor") continue;
            if (c.green > 120 && c.red < 40 && c.blue < 120) {
                hidden.push({ item: item, hidden: item.hidden });
                item.hidden = true;
            }
        } catch (e) {
        }
    }
    return hidden;
}

function restoreHidden(records) {
    for (var i = 0; i < records.length; i++) {
        try { records[i].item.hidden = records[i].hidden; } catch (e) {}
    }
}
