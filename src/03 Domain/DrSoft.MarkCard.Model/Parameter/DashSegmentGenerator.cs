namespace DrSoft.MarkCard.Model
{
    /// <summary>
    /// 虚线段生成工具：根据多组 A/B 参数沿折线轮廓循环迭代生成虚线实线段。
    /// </summary>
    public static class DashSegmentGenerator
    {
        /// <summary>
        /// 根据多组 A/B 参数，沿折线轮廓循环迭代生成虚线实线段（世界坐标）。
        /// <para>
        /// 算法：组按 group0, group1, ..., groupN-1, group0, ... 循环；
        /// 每组内 A=该段总长度（实线+空白），B=实线长度，空白长度=A-B。
        /// 每段结构：先实线(B)，再空白(A-B)，第二段从上一段终止点开始。
        /// C 参数不参与计算。
        /// 当 isOddEvenAlign 为 true 且 evenRowOffset 不为 0 时，
        /// 偶数编号组（0-based 索引为奇数：group1, group3, ...）的起始位置偏移 evenRowOffset。
        /// </para>
        /// </summary>
        public static List<((float X, float Y) Start, (float X, float Y) End)> Generate(
            IReadOnlyList<(double A, double B)> dashGroups,
            IReadOnlyList<(float X, float Y)> vertices,
            bool isClosed,
            bool isOddEvenAlign,
            double evenRowOffset)
        {
            var result = new List<((float X, float Y) Start, (float X, float Y) End)>();
            if (dashGroups == null || dashGroups.Count == 0 || vertices == null || vertices.Count < 2)
                return result;

            int vertexCount = vertices.Count;
            int edgeCount = vertexCount - 1 + (isClosed && vertexCount > 2 ? 1 : 0);

            int groupIndex = 0;
            bool isSolid = true;
            // 初始：第一组的实线长度 = B，空白长度 = A - B
            double solidLen = dashGroups[0].B;
            double gapLen = dashGroups[0].A - dashGroups[0].B;
            float remaining = (float)(solidLen > 0 ? solidLen : 0);
            (float X, float Y) solidStart = (0, 0);
            bool hasSolidStart = false;

            for (int edgeIdx = 0; edgeIdx < edgeCount; edgeIdx++)
            {
                var edgeStart = vertices[edgeIdx];
                var edgeEnd = (isClosed && edgeIdx == vertexCount - 1)
                    ? vertices[0]
                    : vertices[edgeIdx + 1];

                float dx = edgeEnd.X - edgeStart.X;
                float dy = edgeEnd.Y - edgeStart.Y;
                float edgeLen = (float)Math.Sqrt(dx * dx + dy * dy);
                if (edgeLen < 0.001f) continue;

                float ux = dx / edgeLen;
                float uy = dy / edgeLen;

                float pos = 0f;
                if (isOddEvenAlign && (groupIndex % 2 == 1) && Math.Abs(evenRowOffset) > 0.001)
                {
                    pos = (float)evenRowOffset;
                    if (pos >= edgeLen)
                    {
                        hasSolidStart = false;
                        continue;
                    }
                    hasSolidStart = false;
                }

                if (isSolid && !hasSolidStart)
                {
                    solidStart = (edgeStart.X + ux * pos, edgeStart.Y + uy * pos);
                    hasSolidStart = true;
                }

                while (pos < edgeLen - 0.001f)
                {
                    float advance = Math.Min(remaining, edgeLen - pos);
                    pos += advance;
                    remaining -= advance;

                    if (remaining > 0.001f)
                    {
                        // 当前段未消耗完 → 边先结束
                        if (isSolid)
                        {
                            // 实线延续到下一条边：提交本边上的实线部分
                            result.Add((solidStart, (edgeEnd.X, edgeEnd.Y)));
                            hasSolidStart = false;
                        }
                        break; // 移动到下一条边
                    }

                    // 当前段消耗完毕 → 切换状态
                    if (isSolid)
                    {
                        // 实线段结束：提交
                        result.Add((solidStart, (edgeStart.X + ux * pos, edgeStart.Y + uy * pos)));
                        hasSolidStart = false;
                    }

                    // 切换：实线→空白，空白→实线（并切到下一组）
                    isSolid = !isSolid;
                    if (isSolid)
                    {
                        // 空白结束，进入下一组
                        groupIndex++;
                        var group = dashGroups[groupIndex % dashGroups.Count];
                        solidLen = group.B;
                        gapLen = group.A - group.B;
                        solidStart = (edgeStart.X + ux * pos, edgeStart.Y + uy * pos);
                        hasSolidStart = true;
                        remaining = (float)(solidLen > 0 ? solidLen : 0);
                    }
                    else
                    {
                        // 实线结束，进入空白段
                        remaining = (float)(gapLen > 0 ? gapLen : 0);
                    }

                    // 跳过零长度段
                    if (remaining < 0.0001f)
                    {
                        if (isSolid) hasSolidStart = false;
                        isSolid = !isSolid;
                        if (isSolid)
                        {
                            groupIndex++;
                            var group = dashGroups[groupIndex % dashGroups.Count];
                            solidLen = group.B;
                            gapLen = group.A - group.B;
                        }
                        remaining = (float)((isSolid ? solidLen : gapLen));
                        if (remaining < 0.0001f) remaining = 0;
                        if (isSolid && remaining > 0)
                        {
                            solidStart = (edgeStart.X + ux * pos, edgeStart.Y + uy * pos);
                            hasSolidStart = true;
                        }
                    }
                }
            }

            return result;
        }
    }
}
