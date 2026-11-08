using System;
using System.Linq;
using ModePuzzle.Engine;
using ModePuzzle.Game;
using ModePuzzle.Model;
static class ShapeChecks
{
    static int Main()
    {
        int count=0;
        foreach(var cells in new[] {
            new[]{new Cell(0,0),new Cell(1,0),new Cell(0,1)},
            new[]{new Cell(1,1),new Cell(2,1),new Cell(2,2),new Cell(3,2)},
            new[]{new Cell(0,0),new Cell(1,0)},
            new[]{new Cell(1,1)} })
        {
            var module=new ModuleInfo {Shape=cells.ToList(),ShapeWidth=cells.Max(c=>c.X)+2,ShapeHeight=cells.Max(c=>c.Y)+2};
            var board=new Board(7,5,Enumerable.Repeat(true,35).ToArray());
            var data=new byte[module.ShapeWidth*module.ShapeHeight];
            foreach(var cell in cells) data[cell.Y*module.ShapeWidth+cell.X]=1;
            foreach(var p in Geometry.GeneratePlacements(board,EngineBridge.ItemOf(module)))
            {
                var pose=EngineBridge.Proposed(module,p,board.W);
                var shape=new GridShapeBuilder().SetData(data,module.ShapeWidth).SetTransform(pose.OriginX,pose.OriginY,pose.Flipped,pose.Orientation).Build();
                var actual=cells.Select(c=>shape.ForwardTransform(c.X,c.Y)).Select(c=>c.Item2*board.W+c.Item1).OrderBy(c=>c);
                if(!actual.SequenceEqual(p.Cells.OrderBy(c=>c))) throw new Exception("Game transform mismatch");
                count++;
            }
        }
        Console.WriteLine("GAME SHAPE ROUNDTRIP OK: "+count+" placements including padded, flipped and rotated shapes");
        return 0;
    }
}
