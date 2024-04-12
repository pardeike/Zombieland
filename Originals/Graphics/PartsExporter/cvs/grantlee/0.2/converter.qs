var ExportCVS = function(root)
{
    var variant = root.allResults[root.variantIndex];

	 var frames = [];
    for (var i = 0; i < variant.textures.length; i++)
    {
        var texture = variant.textures[i];
		  var totalHeight = texture.size.height;
        for (var j = 0; j < texture.allSprites.length; j++)
        {
            var sprite = texture.allSprites[j];
				var x = sprite.frameRect.x;
				var y = sprite.frameRect.y;
				var w = sprite.frameRect.width;
				var h = sprite.frameRect.height;
				var name = sprite.trimmedName;
            var frameValues = [ x, totalHeight - y - h, w, h, name ];
            frames.push(frameValues.join(","));
        }
    }
    return '#v0.1\n' + frames.join('\n');
}
ExportCVS.filterName = "exportCVS";
Library.addFilter("ExportCVS");
